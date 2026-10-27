# Imagen de Matchmaking: compilación con el SDK y ejecución en el runtime de ASP.NET.
# La versión sigue el TargetFramework net10.0 del proyecto.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/BattleHub.Matchmaking.Api/BattleHub.Matchmaking.Api.csproj src/BattleHub.Matchmaking.Api/
RUN dotnet restore src/BattleHub.Matchmaking.Api/BattleHub.Matchmaking.Api.csproj

COPY src/ src/
RUN dotnet publish src/BattleHub.Matchmaking.Api/BattleHub.Matchmaking.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=5 \
    CMD curl --fail --silent http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "BattleHub.Matchmaking.Api.dll"]
