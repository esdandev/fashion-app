param(
    [string]$ApiKey = $env:AI__APIKEY,
    [ValidateSet('gemini', 'openai')]
    [string]$Family = 'gemini',
    [string]$Model = $(if ($env:AI__MODEL) {
        $env:AI__MODEL
    } elseif ($Family -eq 'openai') {
        'deepseek-v4.1-flash'
    } else {
        'gemini-4-argon'
    }),
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

if ($Family -eq 'openai') {
    $uri = 'https://opencode.ai/inference/openai/v1/chat/completions'
    $payload = @{
        model = $Model
        messages = @(
            @{ role = 'user'; content = 'Respondé solamente: OK' }
        )
    } | ConvertTo-Json -Depth 8
}
else {
    $uri = "https://opencode.ai/inference/google/v1beta/models/${Model}:generateContent"
    $payload = @{
        contents = @(
            @{ parts = @(@{ text = 'Respondé solamente: OK' }) }
        )
    } | ConvertTo-Json -Depth 8
}

Write-Host "Probando OpenCode ($Family) con el modelo '$Model'..." -ForegroundColor Cyan
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
    Write-Host 'Si el error es Account.InsufficientFunds, cargá saldo/Go en Console o usá AI__PROVIDER=gemini.' -ForegroundColor Yellow
    Write-Host "  .\scripts\check-opencode.ps1 -ListModels" -ForegroundColor Yellow
    exit 1
}
