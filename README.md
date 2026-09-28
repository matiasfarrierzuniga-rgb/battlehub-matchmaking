# BattleHub Matchmaking & Lobby Service

Servicio académico de BattleHub desarrollado por el **Equipo 2**.

- Backend: .NET 10
- Base de datos planificada: MongoDB
- Tiempo real planificado: SignalR
- Estado: configuración inicial

## Ejecutar la API

```powershell
dotnet run --project src/BattleHub.Matchmaking.Api
```

## MongoDB

MongoDB es la base de datos del Matchmaking Service.

- Conexión local predeterminada: `mongodb://localhost:27017`
- Base de datos: `battlehub_matchmaking`
- Las credenciales nunca deben versionarse.
- En producción, la configuración puede sobrescribirse mediante `MongoDb__ConnectionString` y `MongoDb__DatabaseName`.
