param(
    [string]$Auth0Domain,
    [string]$Auth0Audience,
    [string]$MongoConnectionString = "mongodb://localhost:27017"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Auth0Domain)) {
    Write-Host "Auth0 Domain es requerido."
    exit 1
}

if ([string]::IsNullOrWhiteSpace($Auth0Audience)) {
    Write-Host "Auth0 Audience es requerido."
    exit 1
}

if ([string]::IsNullOrWhiteSpace($MongoConnectionString)) {
    Write-Host "MongoDB ConnectionString es requerido."
    exit 1
}

$project = Join-Path $PSScriptRoot "..\src\BattleHub.Matchmaking.Api\BattleHub.Matchmaking.Api.csproj"

function Set-Secret([string]$name, [string]$value) {
    dotnet user-secrets set $name $value --project $project
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

Set-Secret "Auth0:Domain" $Auth0Domain
Set-Secret "Auth0:Audience" $Auth0Audience
Set-Secret "MongoDb:ConnectionString" $MongoConnectionString

Write-Host "Matchmaking: http://localhost:5211"
Write-Host "Lobby hub:   http://localhost:5211/hubs/lobby"
Write-Host "CORS:        http://localhost:4000"
Write-Host "MongoDB:     $MongoConnectionString"
Write-Host "Los valores de Auth0 quedaron en user-secrets. No se escribieron en appsettings.json."

Push-Location (Join-Path $PSScriptRoot "..")
try {
    dotnet run --project src/BattleHub.Matchmaking.Api --launch-profile http
}
finally {
    Pop-Location
}
