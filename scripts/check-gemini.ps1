param(
    [string]$ApiKey = $env:AI__APIKEY,
    [string]$Model = $(if ($env:AI__MODEL) { $env:AI__MODEL } else { 'gemini-3.8-flash' }),
    [switch]$ListModels
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    Write-Host 'Falta AI__APIKEY. En PowerShell:' -ForegroundColor Yellow
    Write-Host '  $env:AI__APIKEY="tu-clave"' -ForegroundColor Yellow
    exit 1
}

if ($ListModels) {
    $uri = "https://generativelanguage.googleapis.com/v1beta/models?key=$ApiKey"
    try {
        $response = Invoke-RestMethod -Uri $uri -Method Get -TimeoutSec 30
        $response.models |
            Where-Object { $_.supportedGenerationMethods -contains 'generateContent' } |
            Select-Object -ExpandProperty name
        exit 0
    }
    catch {
        Write-Host "ERROR al listar modelos: $($_.Exception.Message)" -ForegroundColor Red
        if ($_.ErrorDetails.Message) { Write-Host $_.ErrorDetails.Message }
        exit 1
    }
}

$uri = "https://generativelanguage.googleapis.com/v1beta/models/${Model}:generateContent?key=$ApiKey"
$payload = @{
    contents = @(
        @{ parts = @(@{ text = 'Respondé solamente: OK' }) }
    )
} | ConvertTo-Json -Depth 8

Write-Host "Probando modelo '$Model'..." -ForegroundColor Cyan
try {
    $response = Invoke-WebRequest -Uri $uri -Method Post -ContentType 'application/json' `
        -Body $payload -TimeoutSec 30
    Write-Host "OK $($response.StatusCode)" -ForegroundColor Green
    Write-Host $response.Content
}
catch {
    $status = 0
    try { $status = [int]$_.Exception.Response.StatusCode } catch { }
    Write-Host "ERROR $status" -ForegroundColor Red
    if ($_.ErrorDetails.Message) {
        Write-Host $_.ErrorDetails.Message
    } else {
        Write-Host $_.Exception.Message
    }
    Write-Host ''
    Write-Host 'Revisá que la clave sea válida y que el modelo exista. Probá:' -ForegroundColor Yellow
    Write-Host "  .\scripts\check-gemini.ps1 -ListModels" -ForegroundColor Yellow
    exit 1
}
