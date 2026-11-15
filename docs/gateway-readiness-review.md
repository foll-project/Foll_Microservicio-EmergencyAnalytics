# Estado de comunicación y entrada única — 2026-11-15

> Revisión histórica anterior a configurar Kong. Las rutas, CORS, WebSocket y base pública ya se implementaron y comprobaron; consultar el [README actual del gateway](../../Kong%20(Apigateway)/README.md). Siguen siendo relevantes las observaciones sobre contratos del frontend y alcance funcional.

Objetivo revisado: que el frontend utilice únicamente el gateway, conservando los flujos del monolito mediante HTTP y eventos entre los cinco microservicios. Esta revisión no modifica código ni configuración del gateway.

## Dictamen

La comunicación interna principal está implementada, con contratos y credenciales alineados. No está terminada la entrada única del frontend: faltan rutas del gateway, comprobación de CORS y del hub a través de Kong, y un ajuste de la URL pública generada por Notifications. Tampoco puede asegurarse que el frontend necesite únicamente un cambio de base URL: la eliminación de cuenta tiene un contrato asíncrono diferente.

## Cobertura del gateway

Fuente: `../Kong (Apigateway)/kong.yml`.

| Prefijo | Servicio | Estado en Kong |
|---|---|---|
| `/api/iam` | IAM :5047 | Configurado |
| `/api/care` | Care :5245 | Configurado |
| `/api/patients` | Care :5245, rutas de cuidadores por QR | Configurado |
| `/api/devices` | Devices :5256 | Falta |
| `/api/device-managment/debug` | Devices :5256, diagnóstico opcional | Falta |
| `/api/emergency/incidents` | Emergency :5255 | Falta |
| `/api/notifications` | Notifications :5257 | Falta |
| `/api/emergency/public-location` | Notifications :5257 | Falta |
| `/hubs/notifications` | Notifications :5257 | Falta |
| `/internal/patients` | Care :5245 | Publicado; no es necesario para el frontend |

Las rutas de incidentes y ubicación pública pertenecen a procesos distintos y deben dirigirse a sus destinos respectivos. La comunicación interna ya utiliza las URLs directas de los servicios, por lo que el frontend no necesita las rutas internas ni sus llaves.

SignalR requiere tanto la negociación HTTP como la conexión WebSocket por el gateway, con el JWT que utiliza el hub. La prueba previa de handshake se ejecutó contra Notifications directamente; no demuestra ese recorrido a través de Kong.

`plugins: []` en Kong. IAM, Care, Devices y Emergency no registran CORS; Notifications sí tiene una política de orígenes explícitos. Si el frontend corre en otro origen y no usa un proxy del mismo origen, falta configurar/verificar el preflight y los encabezados CORS del acceso por gateway. No se proporcionó el código del frontend para comprobar su estrategia real.

## Llamadas síncronas

| Origen → destino | Operación | Autorización | Revisión |
|---|---|---|---|
| Care → IAM | Datos de cuidadores | JWT del usuario | Ruta pública y gateway alineados |
| Care → Devices | Estado por paciente | JWT del usuario | Ruta y DTO coincidentes |
| Devices → Care | Permiso de vincular/consultar dispositivo | JWT del usuario | Ruta y DTO coincidentes; se comprueba cuidador principal |
| Emergency → Care | Acceso a incidentes del paciente | JWT del usuario | Ruta/DTO y comprobación de pertenencia alineados |
| Emergency → Devices | Asignación tras caída MQTT | Llave específica | Ruta y credenciales coincidentes |
| Notifications → Care | Destinatarios push/SMS/realtime | Llave compartida de contexto | Implementada en consultas de Care |
| Care → IAM | Datos mínimos de destinatarios desde worker | Misma llave de contexto | Ruta mínima protegida, sin solicitar JWT de servicio |
| Notifications → Emergency | Ubicación/estado después de validar enlace | Misma llave de contexto | Ruta protegida y DTO compatibles |

Se compararon sin imprimir valores: la clave JWT coincide en los cinco servicios; las llaves de contexto y de asignación coinciden entre sus emisores y receptores. Las URLs HTTP apuntan a los microservicios en :5047/:5245/:5255/:5256/:5257; no se encontraron URLs activas de estas dependencias que requieran el monolito :5237.

Esta comprobación estática de rutas, DTO y configuración, junto a las pruebas existentes de los gateways, no constituye una prueba de todas las peticiones HTTP encadenadas con los cinco procesos corriendo simultáneamente.

## Comunicación asíncrona

| Emisor → receptor | Flujo | Estado |
|---|---|---|
| Care → Notifications | Invitación creada/aceptada/rechazada | Contratos actuales adaptados al hub |
| Emergency → Notifications | Incidente abierto/cerrado, con nombre de quien cierra | Publicación e inbox verificadas |
| Devices → Notifications | Batería baja/recuperada, desconexión/reconexión | Publicación e inbox verificadas |
| Devices → Notifications | Telemetría | Buffer efímero; descarta snapshots antiguos |
| IAM → Care | Solicitud de eliminación de cuenta | Publicador y consumidor presentes |
| Care → IAM | Confirmación de limpieza | Consumidor completa la eliminación de cuenta |
| Care → Devices | Pacientes eliminados por eliminación de cuenta | Consumidor desvincula los dispositivos asociados |
| Care → Emergency | Pacientes eliminados por eliminación de cuenta | Limpieza e inbox transaccionales verificadas |

Se volvió a ejecutar `dotnet run --project tests/Integration.Checks/Integration.Checks.csproj -- --rabbitmq`: 11 comprobaciones correctas con PostgreSQL y RabbitMQ reales, utilizando y limpiando una base, exchange y colas temporales. Incluye publicación de Emergency/Devices, adaptación de los tres tipos de invitación, recepción durable, duplicados/conflictos, telemetría y consumo de limpieza en Emergency. No recorre Kong ni la aplicación del frontend ni envía SMS/push a proveedores reales.

Los receptores confirman persistencia/procesamiento según su caso. Push/SMS conservan el manejo de fallos del monolito y siguen en Fake para desarrollo; no se amplió el alcance a reenvíos por destinatario. El monolito tiene desactivado por defecto su consumidor de invitaciones para no competir por la cola con Notifications.

## Contrato y diferencias respecto del monolito

1. En el monolito, `DELETE /api/iam/auth` respondía 200 con un mensaje de cuenta eliminada. IAM separado responde 202 con una operación y estado; existe `GET /api/iam/auth/account-deletion`. El frontend debe tratar la operación como asíncrona, no interpretar la aceptación como eliminación ya terminada.
2. `Notifications:PublicBaseUrl` sigue en `http://localhost:5257`. Los enlaces generados evitan el gateway; deben usar su base pública para cumplir la entrada única.
3. La limpieza de datos de Notifications cuando se elimina una cuenta fue excluida expresamente del alcance anterior. La implementación no es idéntica al monolito en ese caso; no se presenta como un fallo accidental ni se implementó sin autorización.
4. No se verificó compatibilidad exacta del frontend porque no se encontró su repositorio entre los proyectos disponibles. Debe comprobarse base URL de API, URL de SignalR, encabezados, manejo de estados y contratos que realmente utiliza.

## Estado de ejecución observado

En esta revisión RabbitMQ estaba levantado. IAM (:5047), Care (:5245), Emergency (:5255), Devices (:5256), Notifications (:5257), Kong (:8000) y el broker MQTT (:1883) no estaban escuchando. Las colas consultadas no tenían consumidores activos. Esto describe el estado de procesos observado y no demuestra un error en la implementación de sus consumidores.

Las caídas, heartbeats y órdenes de cierre del dispositivo requieren el broker MQTT además de RabbitMQ. Los tests de contratos/eventos no reemplazan la prueba del recorrido de MQTT hasta la actualización de la pantalla.

El contenedor RabbitMQ vigente publica sus puertos en todas las interfaces, mientras el Compose guardado especifica loopback; no se ha recreado para aplicar ese enlace de puertos. No se modificaron firewall, procesos ni contenedores durante esta revisión.

## Criterio para considerar terminado el objetivo

1. Completar las rutas públicas de Kong, incluida ubicación pública y SignalR, y dejar las rutas internas para el tráfico entre servicios.
2. Configurar/verificar CORS según el origen real del frontend y cambiar `PublicBaseUrl` de Notifications a la base pública del gateway.
3. Levantar los cinco servicios, Kong y los dos brokers con bases e IDs coherentes.
4. Ejecutar una prueba funcional a través de Kong: login, creación/consulta de paciente, vinculación y estado de dispositivo, invitación y notificación por el hub, caída MQTT, resolución y actualización realtime, enlace público y eliminación asíncrona de cuenta.
5. Contrastar los resultados con el frontend y mantener explícitas las exclusiones de alcance. Hasta entonces, no afirmar que basta cambiar una URL del cliente.
