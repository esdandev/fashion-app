param(
    [string]$Urls = 'http://localhost:5210',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $root 'backend/FashionApp.Api'

Push-Location $projectDir
try {
    Write-Host "Compilando en $Configuration..." -ForegroundColor Cyan
    dotnet build FashionApp.Api.csproj -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    $dll = Join-Path $projectDir "bin/$Configuration/net10.0/FashionApp.Api.dll"
    if (-not (Test-Path $dll)) {
        throw "No se encontró $dll"
    }

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    Write-Host "Iniciando API en $Urls (sin apphost .exe)..." -ForegroundColor Green
    dotnet $dll --urls $Urls
}
finally {
    Pop-Location
}
