# Revisión de NotificationCommunication — 2026-11-14

> Inventario histórico anterior a la corrección. El estado implementado se describe en [implementación](notification-communication-implementation.md); los bloqueos descritos abajo no representan el código posterior.

Revisión de las copias locales de Notifications, Emergency, Devices, Care, IAM y el monolito. Se revisó el código ejecutable y sus contratos; no se tomó el README como evidencia de que existieran adaptadores. No se modificó código ni configuración de Notifications ni se aplicaron sus migraciones.

## Configuración solicitada de Emergency → Devices

La misma llave está ahora en `Services:Devices:ApiKey` del `appsettings.json` de Emergency y en `InternalApi:EmergencyAssignmentKey` del `appsettings.json` de Devices. Se retiraron únicamente esas dos entradas de `dotnet user-secrets` para que no sobrescriban los archivos. Se comprobó igualdad sin imprimir el valor. La llave no se utiliza para Notifications.

## Qué hace el servicio

| Flujo | Implementación actual | Permiso o credencial |
|---|---|---|
| Listar y consultar notificaciones y estado de entrega | HTTP `/api/notifications` y sus subrutas | JWT local; consultas filtradas por `userId` |
| Marcar una notificación leída o reconocida | HTTP `/{id}/read`, `/{id}/acknowledge` | JWT local; búsqueda por notificación y usuario |
| Registrar, listar y desactivar tokens push | HTTP `/api/notifications/push-tokens` | JWT local; el usuario viene del token |
| Conectar al canal en tiempo real | SignalR `/hubs/notifications` | JWT local, grupo `user:{userId}`; el hub admite `access_token` en query para su transporte |
| Recibir apertura y cierre de incidentes | POST `/internal/notifications/emergency-events` | `X-Foll-Service-Key` igual a `InternalApi:ApiKey` |
| Recibir batería baja/recuperada y desconexión/reconexión | POST `/internal/notifications/device-events` | La misma API key general |
| Recibir cambios de invitación | POST `/internal/notifications/invitation-events` | La misma API key; contrato HTTP antiguo |
| Recibir telemetría | POST `/internal/notifications/device-telemetry` | La misma API key; buffer en memoria, sin inbox durable |
| Obtener destinatarios | GET a Care `/internal/patients/{id}/notification-recipients` | Envía la API key general; sin JWT de usuario |
| Obtener nombre de quien cerró un incidente | GET a IAM `/internal/iam/users/{id}` | Envía la misma API key; no solicita un token |
| Resolver ubicación desde un enlace público | Valida token temporal y consulta Emergency `/internal/emergency/incidents/by-key/{key}` | El visitante no requiere JWT; la consulta interna envía la API key general |
| Enviar notificación push/SMS | Firebase/Twilio, o adaptadores Fake según configuración | Credenciales propias del proveedor cuando corresponde |
| Publicar al frontend | SignalR: `notification.created`, `incident.resolved`, `invitation.changed`, `device.telemetry` | Grupos de usuarios destinatarios |

### Qué significa asíncrono aquí

Emergencias y alertas de Devices llegan por HTTP, pero el procesamiento se desacopla mediante una inbox en PostgreSQL:

`outbox del emisor → POST HTTP → inbox de Notifications → 202 → worker → Care/IAM/proveedores/SignalR`.

La respuesta confirma persistencia, no entrega al usuario. El receptor deduplica por `EventId`: un duplicado compatible devuelve 200 y el mismo ID con otro contenido devuelve 409. Valida tipo y payload antes de guardar. El worker intenta mantener el orden de recepción por stream/paciente y reintenta excepciones con espera creciente.

La telemetría conserva solo el dato más reciente por dispositivo en un buffer acotado y descarta datos de más de 30 segundos. Su 202 no garantiza persistencia ni recepción por el frontend. Las llamadas HTTP a Care/IAM y proveedores se esperan dentro del worker.

El servicio de Notifications no registra un consumidor de RabbitMQ ni tiene la dependencia de cliente RabbitMQ en su proyecto actual.

## Bloqueos confirmados

### 1. La consulta de destinatarios no existe en el Care actual

`PatientRecipientsGateway` requiere `/internal/patients/{id}/notification-recipients`. El controlador interno de Care solo implementa `device-access` y `emergency-access`. Tampoco se encontró el puente HTTP prometido en el monolito actual.

Impacto: sin destinatarios no se generan correctamente las notificaciones push/SMS ni se resuelven los grupos para cierre y telemetría. El gateway interpreta 404 como paciente ausente; crear notificaciones lanza una excepción y deja el evento pendiente, mientras cierre y telemetría pueden terminar sin publicar si no hay destinatarios.

Evidencia: `NotificationCommunication/Infrastructure/Gateways/PatientRecipientsGateway.cs`; `Care/Interfaces/Internal/InternalPatientsController.cs`; `NotificationCommandService.Handle(CreateNotificationFromEventCommand)`; publicadores de cierre y telemetría.

### 2. Ruta y autenticación de la consulta a IAM no coinciden

Notifications llama a `/internal/iam/users/{id}` con una API key. El IAM actual expone `/api/iam/users/{id}` bajo JWT de usuario. No se encontró la ruta interna equivalente en IAM ni en el monolito.

Impacto: con el gateway actual, un 404 deja el nombre de cierre vacío. Cambiar solamente la URL a la ruta pública produciría un problema de autenticación: ese endpoint requiere JWT y el worker no lleva uno de usuario. Se necesita un contrato interno autorizado para esta consulta de datos, sin introducir obtención de JWT de servicio.

### 3. Las invitaciones de Care no llegan a este microservicio

Care publica por RabbitMQ `care.invitation-created.v1`, `care.invitation-accepted.v1` y `care.invitation-rejected.v1`. Su contrato usa `EventId` GUID, `OperationId`, versión y `OccurredAtUtc`.

Notifications solo recibe por HTTP `care.invitation-changed.v1` en un `IntegrationEnvelope`, con un ID del formato `care.invitation:{kind}:{invitationId}:{targetUserId}` y payload `InvitationRealtimeEvent`, que requiere otros campos como título, mensaje y nombre del solicitante.

El consumidor de las invitaciones nuevas está registrado en el monolito, que publica a su propio hub SignalR. No publica al hub del microservicio. No bastaría añadir una conexión al broker sin adaptar el contrato y decidir qué proceso consume/publica para evitar competencia o duplicados.

### 4. Los emisores actuales no están alineados con la llave de Notifications

Emergency envía sus eventos sin `X-Foll-Service-Key`. Devices conserva un cliente que solo añade ese encabezado si `InternalApi:ApiKey` tiene valor; en su archivo actual está vacío. La llave `EmergencyAssignmentKey` recién configurada autoriza exclusivamente Emergency → Devices y no resuelve esto.

Además, ambos emisores apuntan Notifications a `localhost:5237`, y Notifications apunta sus tres dependencias al mismo puerto del monolito. Los servicios separados escuchan en otros puertos. No se encontraron los puentes internos prometidos en esa copia del monolito.

Impacto: apuntando directamente a Notifications con una credencial ausente/incorrecta se obtiene 401; manteniendo las URLs actuales, se depende de adaptadores que no están implementados en esta copia.

### 5. Una sola llave general habilita todas las entradas internas

`[InternalApiKey]` protege el controlador completo. El prefijo de tipo se compara con la ruta, pero no hay identidad del emisor ni permisos distintos por origen. Cualquier poseedor de la llave puede enviar un evento válido de cualquiera de los tres BC o telemetría.

La misma llave también se reenvía a Care, IAM y Emergency. Es una diferencia respecto de una llave limitada a Emergency → Devices. Conviene separar credenciales y operaciones por emisor/destino antes de conectar estos flujos, con restricción de red y HTTPS entre máquinas.

### 6. El archivo de configuración no prepara un arranque autónomo

`JwtSettings:Secret` e `InternalApi:ApiKey` están vacíos; `Program.cs` rechaza el arranque si no llegan de otra fuente. La contraseña de conexión está vacía. Las migraciones automáticas están desactivadas también en desarrollo, aunque inbox y telemetría están habilitadas. Sin una base preparada, el worker puede fallar consultando tablas que no existen, como ocurrió con Emergency. No se inspeccionó ni modificó esa base.

Los proveedores configurados son `Fake`: el código puede registrar `Sent` sin enviar un push ni un SMS real. Esto es válido para desarrollo, pero debe tenerse en cuenta al interpretar el estado de entrega.

## Observaciones sobre garantías de entrega

1. **No todo fallo se reintenta.** Los errores de push/SMS capturados se guardan como `Failed`; la inbox puede marcarse `ProcessedWithDeliveryFailures` y no se vuelve a seleccionar. Es distinto de una excepción no capturada, que deja el evento en `RetryPending`. El envío fallido no tiene aquí un worker independiente por destinatario.
2. **Deduplicar recepción no garantiza un solo envío externo.** El worker envía a proveedores/SignalR antes del commit final de PostgreSQL. Si el envío ocurrió y después hay rollback o caída, el siguiente intento puede repetirlo. Esta posibilidad se deduce del orden de operaciones; no se reprodujo un fallo de ese tipo.
3. **No hay exclusión del procesamiento entre réplicas.** La recepción usa lock por EventId, pero el worker no adquiere lease, lock por mensaje ni `FOR UPDATE SKIP LOCKED` antes de procesar. Dos réplicas podrían enviar el mismo pendiente. Es una limitación observable del código, no un fallo reproducido.
4. **El enlace público sí tiene una protección específica.** Token aleatorio de 32 bytes, hash SHA-256 y expiración; no requiere una sesión de cuidador, como en el monolito. Sin embargo, la ruta interna de Emergency que consulta permanece sin filtro propio. Si es accesible directamente, no aplica la validación/expiración del enlace. Debe quedar restringida o protegida de manera independiente.

Parte de la lógica de push, SMS, fallback y registro de fallos ya estaba en el monolito. La inbox durable y la frontera HTTP añaden estados de recepción/procesamiento y nuevos puntos de fallo. No se debe interpretar todo lo distinto como una regresión ni afirmar entrega exactamente una vez.

## Qué se conservó correctamente del monolito

- Las API de usuario validan localmente firma/caducidad y extraen `userId`; no llaman a IAM para validar ni obtener JWT.
- Consultas y cambios de estado buscan por ID de notificación y usuario; los tokens push también se filtran por propietario.
- SignalR añade cada conexión al grupo de su usuario; los publicadores calculan grupos de destinatarios.
- El enlace público conserva su token temporal.
- En el monolito, Care/IAM/Emergency se consultaban mediante clases o repositorios locales. Esos accesos no pasaban por controladores HTTP ni necesitaban JWT de un worker.
- La eliminación de cuenta de IAM hacia Notifications está excluida expresamente en el README de la extracción. Su ausencia no se trata aquí como una implementación accidentalmente olvidada.

## Documentación y verificación

El README de Notifications afirma que existen puentes Care/IAM en el monolito y que se validaron pruebas de transición. Esos tipos y rutas no están en la copia local revisada; esas afirmaciones no describen el estado actual.

Se ejecutó `dotnet test tests/Foll.NotificationCommunication.Tests.csproj`. El ejecutable de Notifications compila, con la advertencia existente CS0618 de Firebase. La suite no llega a ejecutarse: `TransitionTests.cs:3` referencia `foll_backend.Shared.Infrastructure.Integration`, que no existe en el monolito actual (CS0234).

No se enviaron notificaciones reales, no se iniciaron workers ni se modificaron datos de Notifications. Los bloqueos de rutas y contratos se confirmaron comparando código; las posibilidades de duplicado y pérdida se identifican como riesgos derivados del código.

## Orden propuesto de corrección

1. Acordar una credencial y alcance por comunicación interna; conservar JWT local para usuarios.
2. Implementar la consulta de destinatarios en Care y alinear ruta/permiso de datos de usuario en IAM.
3. Alinear URLs y credenciales de Emergency/Devices hacia Notifications.
4. Llevar las invitaciones actuales de RabbitMQ al proceso que aloja el hub de Notifications, adaptando su contrato y evitando dos consumidores competidores por la misma cola.
5. Preparar configuración/base local y actualizar documentación/pruebas para los contratos que realmente existen.
6. Tratar reintentos de entrega, aislamiento de la consulta interna de ubicación y concurrencia de workers según el alcance acordado.
