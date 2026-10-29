# BattleHub Matchmaking & Lobby Service

Servicio de salas y lobby de BattleHub, desarrollado por el **Equipo 2**.

- Backend: .NET 10
- Base de datos: MongoDB
- Tiempo real: SignalR
- La identidad se obtiene del bearer validado de Shell.

Matchmaking administra el ciclo de vida de la sala. No guarda resultados de Typing, Trivia ni Memory.

## Inicio rápido

### Prerrequisitos

- .NET 10 SDK
- MongoDB en `mongodb://localhost:27017`
- Dominio y audiencia de Auth0. No van en el repositorio. Se guardan en user-secrets.

### Variables

| Variable | Equivale a | Ejemplo local |
|---|---|---|
| `Auth0__Domain` | `Auth0:Domain` | `tu-tenant.us.auth0.com` |
| `Auth0__Audience` | `Auth0:Audience` | `https://api.battlehub.local/profile` |
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
  -MongoConnectionString "mongodb://localhost:27017"
```

`-MongoConnectionString` tiene ese valor por defecto. El script publica la API en **http://localhost:5211**, permite el origen del Shell **http://localhost:4000** y deja el lobby en **http://localhost:5211/hubs/lobby**.

Sin `-Auth0Domain` el script responde `Auth0 Domain es requerido.` Sin `-Auth0Audience`, `Auth0 Audience es requerido.` En ambos casos termina antes de `dotnet run`. No pongas Client Secrets ni tokens en el script ni en el README.

### Docker

Copia `.env.example` a `.env` y completa `AUTH0_DOMAIN` y `AUTH0_AUDIENCE`. No pongas Client Secrets en ese archivo.

```powershell
docker compose up --build
```

Compose levanta MongoDB 7 y Matchmaking. Desde el equipo, la API queda en **http://localhost:5211**.

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
dotnet test tests/BattleHub.Matchmaking.Tests/BattleHub.Matchmaking.Tests.csproj --filter "Category=Unit"
dotnet test tests/BattleHub.Matchmaking.Tests/BattleHub.Matchmaking.Tests.csproj --filter "Category=Integration"
dotnet test tests/BattleHub.Matchmaking.Tests/BattleHub.Matchmaking.Tests.csproj --filter "Category=Mongo"
```

`Category=Mongo` usa MongoDB real en `mongodb://localhost:27017` y la base `battlehub_matchmaking_itest`, que se borra al terminar. No sustituye el almacén por una implementación en memoria. Si MongoDB no está levantado, esa prueba falla. El pipeline de GitHub Actions levanta MongoDB 7 para ejecutarla.
