param(
    [string]$ApiKey = $env:AI__APIKEY,
    [string]$Model = $(if ($env:AI__MODEL) { $env:AI__MODEL } else { 'gemini-4-argon' }),
    [switch]$ListModels
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    Write-Host 'Falta AI__APIKEY. En PowerShell:' -ForegroundColor Yellow
    Write-Host '  $env:AI__APIKEY="tu-clave-de-opencode"' -ForegroundColor Yellow
    exit 1
}

$headers = @{ Authorization = "Bearer $ApiKey" }

if ($ListModels) {
    try {
        $response = Invoke-RestMethod -Uri 'https://opencode.ai/inference/v1/models' `
            -Headers $headers -Method Get -TimeoutSec 30
        $response.data | Select-Object -ExpandProperty id
        exit 0
    }
    catch {
        Write-Host "ERROR al listar modelos: $($_.Exception.Message)" -ForegroundColor Red
        if ($_.ErrorDetails.Message) { Write-Host $_.ErrorDetails.Message }
        exit 1
    }
}

$uri = "https://opencode.ai/inference/google/v1beta/models/${Model}:generateContent"
$payload = @{
    contents = @(
        @{ parts = @(@{ text = 'Respondé solamente: OK' }) }
    )
} | ConvertTo-Json -Depth 8

Write-Host "Probando OpenCode con el modelo '$Model'..." -ForegroundColor Cyan
try {
    $response = Invoke-WebRequest -Uri $uri -Method Post -Headers $headers `
        -ContentType 'application/json' -Body $payload -TimeoutSec 30
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
    Write-Host 'Recordá: la clave es una API key personal de OpenCode Console (no la sesión del agente).' -ForegroundColor Yellow
    Write-Host 'Los modelos free solo funcionan dentro de OpenCode; usá un modelo pago o Go.' -ForegroundColor Yellow
    Write-Host "  .\scripts\check-opencode.ps1 -ListModels" -ForegroundColor Yellow
    exit 1
}
