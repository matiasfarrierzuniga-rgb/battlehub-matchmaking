# BattleHub Matchmaking & Lobby Service

Servicio de salas y lobby de BattleHub, desarrollado por el **Equipo 2**.

- Backend: .NET 10
- Base de datos: MongoDB
- Tiempo real: SignalR
- La identidad se obtiene del bearer validado de Shell.

Matchmaking administra el ciclo de vida de la sala. No guarda resultados de Typing, Trivia ni Memory.

## Inicio rápido

### Requisitos

Docker con Compose y un dominio Auth0 válido. La API valida JWT con el dominio y la audiencia configurados; no necesita Client Secrets. Para las pruebas locales también se necesita el SDK .NET 10 y, para cobertura, Python 3.

```powershell
Copy-Item .env.example .env
# Editar .env y completar AUTH0_DOMAIN
docker compose up --build
```

El comando construye y levanta MongoDB 7, el generador de datos y Matchmaking. Para detener los servicios, conservando los datos:

```powershell
docker compose down
```

Para eliminar también el volumen de este proyecto y sus datos, usar `docker compose down -v`.

### Configuración

Las variables están en `.env`, que no se versiona. No incluir tokens ni Client Secrets.

| Variable | Valor local | Uso |
|---|---|---|
| `AUTH0_DOMAIN` | completar | Dominio del tenant para validar JWT |
| `AUTH0_AUDIENCE` | `https://api.battlehub.local/profile` | Audiencia del bearer compartido |
| `MONGO_CONNECTION_STRING` | `mongodb://mongodb:27017` | Conexión desde los contenedores |
| `MONGO_DATABASE` | `battlehub_matchmaking` | Base usada por API y generador |
| `MATCHMAKING_PORT` | `5211` | Puerto de la API en el host |
| `MONGO_PORT` | `27017` | Puerto de MongoDB en el host |
| `CORS_ORIGIN` | `http://localhost:4000` | Origen permitido del Shell |

La API escucha en el puerto 8080 dentro del contenedor y MongoDB en el 27017. Con los valores predeterminados, verificar la disponibilidad desde el host con:

```powershell
curl.exe http://localhost:5211/health
```

El healthcheck de la API consulta ese endpoint desde el contenedor. MongoDB tiene su propio healthcheck; el generador espera a MongoDB y la API espera la finalización exitosa del generador.

### Datos de demostración

El servicio independiente `data-generator` ejecuta `docker/demo-data.js` con `mongosh` y crea en `matches` tres salas `Waiting` para `typing`, `trivia` y `memory`, cada una con su anfitrión. Sus IDs son `demo-typing`, `demo-trivia` y `demo-memory`. Son identidades ficticias, no cuentas de Auth0. El generador usa IDs fijos y `$setOnInsert`: si una sala ya existe, no la modifica ni crea duplicados. Si fue eliminada, la recrea en el siguiente arranque. Las salas están sujetas a las reglas normales de inactividad y limpieza, por lo que pueden desaparecer mientras el sistema permanece encendido. El volumen conserva los datos entre `down` y `up`.

## Direcciones locales

| Qué | Dónde |
|---|---|
| API | http://localhost:5211 |
| Salud | http://localhost:5211/health |
| Lobby hub | http://localhost:5211/hubs/lobby |
| CORS del Shell | http://localhost:4000 |
| MongoDB | mongodb://localhost:27017, base `battlehub_matchmaking` |

El hub no cambió de ruta. El cliente llama `JoinLobby`, `JoinMatch`, `LeaveMatch` y `Heartbeat`.

## Rutas

| Acción | Método | Ruta | Actor | Permiso |
|---|---|---|---|---|
| Listar salas | GET | `/api/matches` | Usuario | Sesión de usuario |
| Detalle | GET | `/api/matches/{matchId}` | Usuario | Sesión de usuario |
| Crear sala | POST | `/api/matches` | Usuario | Sesión de usuario |
| Unirse | POST | `/api/matches/{matchId}/join` | Usuario | Sesión de usuario y capacidad disponible |
| Iniciar | POST | `/api/matches/{matchId}/start` | Dueño de la sala | Sesión de usuario, al menos 2 participantes |
| Finalizar | POST | `/api/matches/{matchId}/finish` | Usuario autenticado desde Shell | Participante de la sala |
| Lobby | SignalR | `/hubs/lobby` | Usuario | Sesión de usuario |

Solo se pueden crear salas `typing`, `trivia` y `memory` (sin distinguir mayúsculas). La capacidad es genérica y debe ser mayor que cero. El nombre visible usa el claim `name` del token cuando existe y, si falta, la identidad del usuario. Matchmaking no consulta Profile ni permisos externos. Las rutas protegidas requieren un bearer válido.

Iniciar exige que el llamador sea el dueño, que la sala esté en `Waiting` y que haya al menos 2 participantes. Con un solo jugador la respuesta es **409** y la sala sigue en `Waiting`. La regla vale para cualquier juego; con 3 o más participantes se puede iniciar si la capacidad de la sala lo permite.

## Historial de partidas

`GET /api/matches/history` devuelve las partidas del usuario autenticado. La identidad sale del token. Un `userId` en la query no cambia de quién es el historial. Sin token, o con un token inválido, la respuesta es **401**. No existe un permiso `matches.history`: basta la sesión. Si el usuario no tiene partidas, la respuesta es **200** y `items` viene vacío.

La colección activa `matches` no sirve como historial permanente: las salas `Finished` y `Cancelled` se eliminan cuando vence `Cleanup:RetentionSeconds`. Por eso, al terminar o al cancelar una partida que ya había iniciado, Matchmaking guarda una copia en `matchHistory`. Una sala que se cancela en `Waiting` o `Starting` no entra, porque la partida no llegó a empezar. Una sala `Started` que sigue en curso tampoco entra hasta que termina o el limpieza la cancela.

El orden es el momento de cierre, del más reciente al más antiguo. `page` empieza en 1 y `pageSize` vale 20, con máximo 50. `gameType` es opcional y solo acepta `typing`, `trivia` y `memory`. Otro valor responde **400**.

```http
GET /api/matches/history?gameType=memory&page=1&pageSize=20
Authorization: Bearer $TOKEN
```

```json
{
  "items": [
    {
      "matchId": "match-1",
      "gameType": "memory",
      "status": "Finished",
      "maxPlayers": 2,
      "participantCount": 2,
      "isHost": true,
      "createdAt": "2026-10-27T18:00:00Z",
      "startedAt": "2026-10-27T18:05:00Z",
      "finishedAt": "2026-10-27T18:12:00Z",
      "cancelledAt": null
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1
}
```

`isHost` indica si el usuario que consulta creó la sala. No se devuelven identificadores, nombres ni correos de los demás participantes. No hay ganador ni puntaje: `/finish` no recibe esos datos. El marcador de Typing, el resultado de Trivia y las parejas de Memory siguen en cada juego. Documentos anteriores, sin `startedAt`, `finishedAt` o `cancelledAt`, se leen como fechas nulas.

## Finalización desde Shell

El juego notifica a Shell mediante un evento y Shell llama a Matchmaking con el bearer del usuario. Los juegos no llaman directamente a Matchmaking.

```http
POST /api/matches/{matchId}/finish
Authorization: Bearer <token del usuario>
```

No lleva cuerpo. Puede finalizar cualquier participante de la sala, incluido el host; un usuario ajeno recibe 403, incluso en un reintento. El contrato consultado no asigna esta acción exclusivamente al host: esta elección usa la pertenencia ya mantenida por el dominio, que se congela al iniciar; el inicio y la cancelación siguen reservados al dueño.

La primera llamada válida cambia `Started` a `Finished`, emite `MatchFinished` y registra el historial. Una llamada posterior válida devuelve 204 sin otra transición ni evento; el registro de historial se vuelve a asegurar mediante upsert por ID para recuperar una falla previa. Otros estados o una sala iniciada expirada devuelven 409; una sala inexistente devuelve 404 y un token ausente o inválido, 401. Los timestamps no cambian en el reintento. No se reciben resultados de juego.

## Pruebas

```powershell
dotnet restore BattleHub.Matchmaking.slnx
dotnet build BattleHub.Matchmaking.slnx --no-restore
dotnet format BattleHub.Matchmaking.slnx --verify-no-changes --no-restore
dotnet test tests/BattleHub.Matchmaking.UnitTests/BattleHub.Matchmaking.UnitTests.csproj
dotnet test tests/BattleHub.Matchmaking.IntegrationTests/BattleHub.Matchmaking.IntegrationTests.csproj
```

Las pruebas Mongo usan MongoDB real en `mongodb://localhost:27017` y la base `battlehub_matchmaking_itest`, que se borra al terminar. Si MongoDB no está levantado, fallan. CI levanta MongoDB 7 y ejecuta ambos proyectos, lint, cobertura y `docker build .`. El workflow adicional ejecuta build y unitarias.

Para reproducir la cobertura combinada de CI, con Python 3 y MongoDB disponibles:
Usa un directorio `TestResults` vacío para no mezclar ejecuciones anteriores.

```powershell
dotnet test tests/BattleHub.Matchmaking.UnitTests/BattleHub.Matchmaking.UnitTests.csproj --collect:"XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults/unit
dotnet test tests/BattleHub.Matchmaking.IntegrationTests/BattleHub.Matchmaking.IntegrationTests.csproj --collect:"XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults/integration
dotnet tool install --global dotnet-reportgenerator-globaltool --version 5.5.1
reportgenerator '-reports:TestResults/**/coverage.cobertura.xml' '-targetdir:TestResults/report' '-reporttypes:Cobertura;Html' '-filefilters:-**/*.generated.cs'
python scripts/coverage-summary.py TestResults/report/Cobertura.xml TestResults/coverage-summary.md
```

El reporte HTML queda en `TestResults/report`; el último comando falla si la cobertura de líneas o de ramas es inferior al 85 %.
