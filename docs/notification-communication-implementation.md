# Implementación acordada — 2026-11-14

Esta entrega sustituye los bloqueos descritos en la revisión anterior. Se conservaron las reglas del monolito y los patrones actuales de Care: consultas en servicios de aplicación, gateways HTTP, outbox, confirmaciones RabbitMQ y consumidores con inbox/ACK.

## Nombre de quien cierra la emergencia

IAM ya emite `firstName` y `lastName` en el JWT. Emergency obtiene esos claims después de validar el JWT y los pasa al comando de resolución/falso positivo. `ClosedByName` se serializa en el evento guardado en la outbox, dentro del mismo guardado del incidente. El cuerpo HTTP no puede elegir ese nombre.

Notifications usa el dato del evento en `incident.resolved`; se eliminó su gateway a IAM. Los eventos anteriores sin el campo conservan nombre nulo y no disparan una consulta adicional. Una cancelación por botón no inventa identidad de usuario.

## Destinatarios en Care

La lógica de `PatientNotificationAcl` del monolito se migró a `PatientQueryService.Handle(GetPatientNotificationRecipientsQuery)`, siguiendo el mismo patrón de las consultas internas actuales de Care.

Conserva cuidador principal, cuidadores asociados, cuidador actual solo si está autorizado, deduplicación, nombres/teléfonos y contactos de emergencia. Un usuario ausente en IAM conserva el fallback `Usuario {id}`. Se expone en `GET /internal/patients/{patientId}/notification-recipients`.

Care usa su `IUserInfoGateway` para obtener los datos de los usuarios. Las peticiones de usuario siguen enviando su JWT a `/api/iam/users/{id}`. La consulta específica de destinatarios, que nace de un worker sin sesión, usa `/internal/iam/users/{id}/notification-recipient`, protegido y limitado a ID, nombre y teléfono. No devuelve correo ni hash de contraseña. No se solicita JWT de servicio.

## Autorización

| Contexto | Autorización |
|---|---|
| Peticiones de usuarios a API/SignalR | JWT local y reglas de acceso correspondientes |
| Consulta de asignación Emergency → Devices desde MQTT | Llave específica acordada en `Services:Devices:ApiKey` / `InternalApi:EmergencyAssignmentKey` |
| Destinatarios Notifications → Care | `InternalApi:BackgroundQueryKey` |
| Datos mínimos de esos destinatarios Care → IAM | La misma llave de consultas de contexto |
| Ubicación mínima Notifications → Emergency después de validar un enlace | La misma llave de consultas de contexto |
| Eventos RabbitMQ | Credenciales y permisos del broker; no se incluyen JWT ni llaves HTTP en los mensajes |

Las llaves están visibles en `appsettings.json`, como se solicitó para desarrollo. Se usa un único valor compartido de desarrollo para estas excepciones, no un sistema de credenciales/identidad por microservicio. Los filtros rechazan ausencia, valor incorrecto y configuraciones vacías/cortas. Las rutas de usuario mantienen JWT; el prefijo `internal` no concede acceso por sí mismo.

Se retiró la API key general de Notifications y sus receptores HTTP de eventos. También se retiró el POST HTTP anónimo de limpieza de pacientes de Emergency.

## Publicación y consumo

Exchange topic durable `foll.integration`.

| Emisor | Evento | Receptor / cola |
|---|---|---|
| Care | `care.invitation-created.v1`, `care.invitation-accepted.v1`, `care.invitation-rejected.v1` | Notifications / `notificationcommunication.invitation-events.v1` |
| Emergency | `emergency-analytics.incident-opened.v1`, `emergency-analytics.incident-closed.v1` | Notifications / `notificationcommunication.emergency-events.v1` |
| Devices | Batería baja/recuperada, desconexión/reconexión | Notifications / `notificationcommunication.device-events.v1` |
| Devices | `device-managment.telemetry.v1` | Notifications / misma cola, buffer efímero sin inbox durable |
| Care | `care.account-deletion-completed.v1` | Emergency / `emergency.care-account-deletion-completed.v1` |

Emergency y Devices usan el patrón de conexión/publicación de Care: conexión/canal reutilizados, exclusión del uso concurrente del canal, eventos persistentes, `mandatory`, confirmación del broker y conservación del pendiente si falla la publicación. Los IDs son deterministas sobre la identidad original del evento y no cambian al reintentar. Los sobres conservan `EventId`, `OperationId`, `OccurredAtUtc`, `Version` y metadatos AMQP concordantes.

Notifications adapta los contratos actuales de Care al mensaje de SignalR y conserva los IDs estables de su inbox para evitar reenviar eventos ya recibidos. Para alertas/invitaciones confirma al broker después de persistir la aceptación, y después procesa el worker. Emergency confirma la limpieza y su inbox en una sola transacción antes de ACK. La telemetría conserva caducidad de 30 segundos y el buffer limitado; no genera una outbox ni una garantía de entrega final.

El monolito deja de competir por la cola de invitaciones: `NotificationCommunication:LocalInvitationConsumerEnabled=false`. El frontend debe conectarse al hub de Notifications, no al hub del monolito, para recibir los mensajes del proceso migrado.

Notifications publica al frontend/proveedores; no se inventaron eventos de negocio salientes ni se migró el caso excluido de IAM → Notifications por cuenta eliminada. Tampoco se agregó el caso excluido de borrado de paciente en Devices.

## Reintentos y desarrollo

Se conservan los fallos capturados push/SMS como `Failed`, sin un nuevo worker de reenvío por destinatario. El cierre SignalR conserva el manejo de errores del monolito. El transporte RabbitMQ sigue los reintentos/cuarentena del consumidor de Care; no se promete entrega exactamente una vez de efectos externos.

Se actualizó la configuración local de puertos: IAM :5047, Care :5245, Emergency :5255, Devices :5256 y Notifications :5257. Los JWT se validan localmente con la misma llave. Push/SMS siguen en Fake.

Se verificaron las credenciales efectivas del contenedor local preexistente `foll-rabbitmq`, se alinearon los JSON de los servicios y se inició ese contenedor. Su virtual host efectivo es `/`. También se alinearon el `.env` local y la variable `RABBITMQ_VHOST` del Compose con ese estado efectivo y se validó la configuración resuelta. No se recreó el contenedor ni se alteraron usuarios o volúmenes.

En PostgreSQL local se aplicó `AddCareEventInbox` en `foll_emergency` y se creó `foll_notifications` con su migración inicial. Development aplica migraciones antes de iniciar workers. No se copiaron datos históricos ni se aplicaron cambios a un despliegue externo.

## Evidencia

- Emergency: 35 pruebas, incluida la comprobación de que el nombre viene del JWT y no del cuerpo HTTP.
- Devices: 29 pruebas; se retiraron las referencias de pruebas a proxies inexistentes del monolito.
- Notifications: 28 pruebas, con emisores reales de Emergency/Devices contra sus contratos receptores y verificación de las tres suscripciones independientes.
- Care: 66 comprobaciones con SQLite y RabbitMQ real, incluidas reglas de destinatarios, credenciales de consultas y publicación de invitaciones.
- IAM: 26 comprobaciones, incluido el contrato mínimo de destinatarios.
- Integración PostgreSQL + RabbitMQ real: 11 comprobaciones en una base, exchange y colas temporales propios. Se comprobó aceptación durable, deduplicación, rechazo de conflicto, publicación real de Emergency/Devices, adaptación de invitaciones, buffer de telemetría y consumo de limpieza en Emergency. Los recursos temporales se eliminan al terminar.
- Los smoke tests verifican arranque, JWT y tablas; Notifications también verifica el handshake SignalR. No se enviaron SMS/push reales.

Comandos:

```powershell
# Desde Emergency
dotnet test tests/Foll.EmergencyAnalytics.Tests.csproj
dotnet run --project tests/Integration.Checks/Integration.Checks.csproj -- --rabbitmq
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Smoke-Test.ps1 -CheckDatabase

# Desde Notifications
dotnet test tests/Foll.NotificationCommunication.Tests.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Smoke-Test.ps1 -CheckDatabase

# Desde Care
dotnet run --project tests/Care.V1.Checks/Care.V1.Checks.csproj -- --rabbitmq
```
