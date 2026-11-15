# Foll — EmergencyAnalytics

Microservicio de emergencias: MQTT, incidentes, historial, resolución/falsos positivos, outbox y comunicación con otros BC. Sigue los adaptadores, contratos versionados, confirmaciones del broker e inbox de Care. No solicita JWT de servicio a IAM.

## Comunicaciones actuales

| Operación | Transporte y autorización |
|---|---|
| API de cuidadores `/api/emergency/incidents` | JWT validado localmente; permisos del paciente consultados en Care con el mismo JWT |
| Asignación de dispositivo al recibir una caída MQTT | HTTP a Devices con `Services:Devices:ApiKey`, igual a `InternalApi:EmergencyAssignmentKey` en Devices |
| Apertura/cierre de incidente | Outbox → RabbitMQ → Notifications; sin JWT de usuario ni API key en el evento |
| Limpieza de pacientes eliminados por Care | Consume `care.account-deletion-completed.v1` por RabbitMQ, con inbox transaccional |
| Ubicación mínima para un enlace público | GET `/internal/emergency/incidents/by-key/{key}`, con `InternalApi:BackgroundQueryKey` |
| Aviso de cierre hacia el dispositivo | MQTT, conservando el comportamiento del monolito |

El JWT incluye `firstName` y `lastName`. El controlador toma esos claims autenticados al resolver o marcar falso positivo y los guarda como `ClosedByName` en la outbox. El cuerpo de la petición no permite elegir ese nombre. Una cancelación por botón no inventa un usuario; los eventos pendientes antiguos pueden tener el nombre nulo. Notifications utiliza el valor del evento y no consulta IAM para obtenerlo.

## RabbitMQ

Exchange topic durable `foll.integration`. Publicaciones: `emergency-analytics.incident-opened.v1` y `emergency-analytics.incident-closed.v1`; cola receptora `notificationcommunication.emergency-events.v1`.

El sobre tiene `EventId` GUID determinista, `OperationId` (IncidentKey), `OccurredAtUtc`, `EventType`, `Version=1` y `Payload`. Las propiedades AMQP mantienen MessageId/CorrelationId/Type. La outbox conserva su contenido, identifica el evento de forma estable y solo marca publicación tras la confirmación del broker; un fallo conserva el pendiente.

Cola de consumo `emergency.care-account-deletion-completed.v1`. La limpieza de incidentes/outbox y la recepción idempotente se confirman en la misma transacción antes del ACK, siguiendo el patrón de Care. Se retiró el POST HTTP anónimo de limpieza. El consumidor utiliza los reintentos y cuarentena `.dead` del patrón existente de Care; no agrega reintentos individuales de push/SMS.

## Desarrollo local

Las URLs actuales están en `appsettings.json`: Care :5245 y Devices :5256. Las llaves de desarrollo están en los archivos como pidió el usuario; no hay entradas de user-secrets que las sobrescriban. `InternalApi:BackgroundQueryKey` es una llave compartida para consultas específicas que nacen sin sesión de usuario, no una identidad por microservicio.

La clave JWT debe coincidir con IAM. Los servicios deben usar la misma configuración RabbitMQ de Care. Levantar PostgreSQL, RabbitMQ y el broker MQTT antes de procesar el flujo completo.

```powershell
dotnet tool restore
dotnet ef database update
dotnet run --launch-profile http
```

`ApplyMigrationsOnStartup=true` en Development y false por defecto fuera de desarrollo. En la base local `foll_emergency` se aplicaron la migración inicial y `AddCareEventInbox`. Tablas propias: `fall_types`, `emergency_incidents`, `outbox_messages` e `integration_inbox_messages`, en el esquema `emergency`.

Puerto 5255. Swagger en desarrollo. `/health/live` comprueba el proceso; `/health/ready` consulta las cuatro tablas. Los perfiles locales escuchan en localhost; no se modificaron firewall ni redes de despliegue.

## Verificación

```powershell
dotnet test tests/Foll.EmergencyAnalytics.Tests.csproj
dotnet run --project tests/Integration.Checks/Integration.Checks.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Smoke-Test.ps1 -CheckDatabase
```

Las comprobaciones PostgreSQL usan una base temporal propia y la eliminan al terminar. Con `--rabbitmq` también crean exchange/colas temporales y verifican publicación y consumo reales, sin enviar alertas a proveedores. El smoke desactiva workers y migraciones. No se enviaron alertas reales ni se copiaron datos del monolito.

La transferencia manual de datos sigue disponible en `scripts/Transfer-EmergencyData.ps1`; no se ejecutó como parte de estos cambios. No se implementaron proxies inexistentes del monolito. Para el inventario y los cambios entre BC, ver [implementación](docs/notification-communication-implementation.md).
