# BattleHub Matchmaking & Lobby Service

Servicio de salas y lobby de BattleHub, desarrollado por el **Equipo 2**.

- Backend: .NET 10
- Base de datos: MongoDB
- Tiempo real: SignalR
- Perfil y permisos: Profile Service (Equipo 1)

Matchmaking administra el ciclo de vida de la sala. No guarda resultados de Typing, Trivia ni Memory.

## Inicio rápido

### Prerrequisitos

- .NET 10 SDK
- MongoDB en `mongodb://localhost:27017`
- Profile Service en `http://localhost:5220` para crear o unirse a salas (si Profile no responde, la operación devuelve 503)
- Dominio y audiencia de Auth0. No van en el repositorio. Se guardan en user-secrets.

### Variables

| Variable | Equivale a | Ejemplo local |
|---|---|---|
| `Auth0__Domain` | `Auth0:Domain` | `tu-tenant.us.auth0.com` |
| `Auth0__Audience` | `Auth0:Audience` | `https://api.battlehub.local/profile` |
| `Profile__BaseUrl` | `Profile:BaseUrl` | `http://localhost:5220` |
| `Cors__Origins__0` | `Cors:Origins[0]` | `http://localhost:4000` |
| `MongoDb__ConnectionString` | `MongoDb:ConnectionString` | `mongodb://localhost:27017` |
| `MongoDb__DatabaseName` | `MongoDb:DatabaseName` | `battlehub_matchmaking` |

`dotnet run` no arranca si faltan `Auth0:Domain` o `Auth0:Audience`. El script local los guarda en user-secrets del proyecto (`UserSecretsId` propio de Matchmaking, distinto al de Profile) y no los escribe en `appsettings.json`.

### Arranque en el equipo

MongoDB debe estar escuchando en el puerto 27017. Luego:

```powershell
.\scripts\Start-Local.ps1 `
  -Auth0Domain "tu-tenant.us.auth0.com" `
  -Auth0Audience "https://api.battlehub.local/profile" `
  -ProfileBaseUrl "http://localhost:5220" `
  -MongoConnectionString "mongodb://localhost:27017"
```

`-ProfileBaseUrl` y `-MongoConnectionString` tienen esos valores por defecto. El script publica la API en **http://localhost:5211**, permite el origen del Shell **http://localhost:4000** y deja el lobby en **http://localhost:5211/hubs/lobby**.

Sin `-Auth0Domain` el script responde `Auth0 Domain es requerido.` Sin `-Auth0Audience`, `Auth0 Audience es requerido.` En ambos casos termina antes de `dotnet run`. No pongas Client Secrets ni tokens en el script ni en el README.

### Docker

Copia `.env.example` a `.env` y completa `AUTH0_DOMAIN` y `AUTH0_AUDIENCE`. No pongas Client Secrets en ese archivo.

```powershell
docker compose up --build
```

Compose levanta MongoDB 7 y Matchmaking. Desde el equipo, la API queda en **http://localhost:5211**. Dentro de la red de Compose, Profile se alcanza por `PROFILE_BASE_URL` (por defecto `http://host.docker.internal:5220`).

## Direcciones locales

| Qué | Dónde |
|---|---|
| API | http://localhost:5211 |
| Salud | http://localhost:5211/health |
| Lobby hub | http://localhost:5211/hubs/lobby |
| CORS del Shell | http://localhost:4000 |
| Profile Service | http://localhost:5220 |
| MongoDB | mongodb://localhost:27017, base `battlehub_matchmaking` |

El hub no cambió de ruta. El cliente llama `JoinLobby`, `JoinMatch`, `LeaveMatch` y `Heartbeat`.

## Rutas

| Acción | Método | Ruta | Actor | Permiso |
|---|---|---|---|---|
| Listar salas | GET | `/api/matches` | Usuario | Sesión de usuario |
| Detalle | GET | `/api/matches/{matchId}` | Usuario | Sesión de usuario |
| Crear sala | POST | `/api/matches` | Usuario | `matches.create` y el permiso del juego |
| Unirse | POST | `/api/matches/{matchId}/join` | Usuario | Permiso de juego de la sala guardada |
| Iniciar | POST | `/api/matches/{matchId}/start` | Dueño de la sala | Sesión de usuario, al menos 2 participantes |
| Finalizar | POST | `/api/matches/{matchId}/finish` | Servicio de juego (M2M) | `matches.finish` y Client ID del `gameType` |
| Lobby | SignalR | `/hubs/lobby` | Usuario | Sesión de usuario |

Solo se pueden crear salas `typing`, `trivia` y `memory` (sin distinguir mayúsculas). Otro valor, vacío o nulo, responde **400** y no guarda la sala. Memory exige `maxPlayers` exactamente **2**. Pedir 1, 4 o 10 responde **400** con el mensaje `Memory requires exactly 2 players.` Typing y Trivia conservan la capacidad enviada si es mayor que 0.

Los permisos del juego son `games.typing.play`, `games.trivia.play` y `games.memory.play`. Matchmaking los lee en `GET /api/profiles/me/permissions` con el mismo Bearer del usuario. No se aceptan desde el cuerpo del request. Crear exige `matches.create` y el permiso del juego. Unirse exige solo el permiso del `gameType` ya guardado en la sala. Si Profile no responde, agota el tiempo o devuelve 5xx, la operación no se completa y la respuesta es **503**. Si Profile responde y falta un permiso, la respuesta es **403**.

Listar y ver el detalle exigen la misma sesión de usuario que el resto de acciones del jugador. Sin token, o con un token inválido, la respuesta es **401**. No existe un permiso `matches.read`. El endpoint `/finish` sigue siendo de máquina y no consulta permisos de persona en Profile.

Iniciar exige que el llamador sea el dueño, que la sala esté en `Waiting` y que haya al menos 2 participantes. Con un solo jugador la respuesta es **409** y la sala sigue en `Waiting`. La regla vale para Typing, Trivia y Memory. Con 3 o más participantes, Typing y Trivia pueden iniciar si la capacidad de la sala lo permite.

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

## Contrato para servicios de juego

Typing, Trivia y Memory avisan el fin de la partida. No envían resultados, ganador ni puntaje.

```http
POST /api/matches/{matchId}/finish
Authorization: Bearer $TOKEN
```

No hay cuerpo. `$TOKEN` es un access token de Auth0 obtenido con client credentials, con audiencia de Matchmaking y el permiso `matches.finish`.

El Client ID del token (`azp`) debe estar autorizado para el `gameType` de la sala:

| Juego | Client ID | gameType |
|---|---|---|
| Typing | `8BWcE4T8HxhJrxU1CtgmNkjDOpkrN4Su` | `typing` |
| Trivia | `xYhNYG9lOGLA6KvEM0Y4cccH2f7JDD5x` | `trivia` |
| Memory | `lfcBlgOCs9N0w6F6AWl4FqOs4xF9AQiN` | `memory` |

Esos identificadores son públicos de configuración. El Client Secret no se versiona.

La transición válida es `Started` → `Finished`. Si la sala ya está `Finished`, la misma llamada responde **204** y no emite otro evento. Cualquier otro estado, incluida `Cancelled`, responde **409**.

| HTTP | Cuándo |
|---|---|
| 204 | La sala pasó a `Finished`, o ya estaba `Finished` |
| 401 | No hay token o no es válido |
| 403 | El token no tiene `matches.finish`, no es un cliente M2M, o el Client ID no corresponde al `gameType` |
| 404 | No existe esa sala |
| 409 | La sala no está en `Started` (y tampoco está ya `Finished`) |
| 503 | MongoDB no está disponible |

401, 403, 404 y 409 son rechazos permanentes: reintentar la misma llamada no va a cambiar el resultado. 408, 429 y 5xx son transitorios; el juego puede reintentar. Matchmaking no implementa esa cola de reintentos.

```bash
curl -X POST "http://localhost:5211/api/matches/MATCH_ID/finish" \
  -H "Authorization: Bearer $TOKEN"
```

## Pruebas

```powershell
dotnet test tests/BattleHub.Matchmaking.Tests/BattleHub.Matchmaking.Tests.csproj --filter "Category=Unit"
dotnet test tests/BattleHub.Matchmaking.Tests/BattleHub.Matchmaking.Tests.csproj --filter "Category=Integration"
dotnet test tests/BattleHub.Matchmaking.Tests/BattleHub.Matchmaking.Tests.csproj --filter "Category=Mongo"
```

`Category=Mongo` usa MongoDB real en `mongodb://localhost:27017` y la base `battlehub_matchmaking_itest`, que se borra al terminar. No sustituye el almacén por una implementación en memoria. Si MongoDB no está levantado, esa prueba falla. El pipeline de GitHub Actions levanta MongoDB 7 para ejecutarla.
