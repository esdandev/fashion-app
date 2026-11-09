# Smoke de producción local — Fase 4

**Fecha:** 2026-11-09
**Entorno:** Windows, .NET 10.0.403, Node 24.22.1, pnpm 12.18.0.
**Objetivo:** validar el mismo artefacto que se publicaría (SPA compilada
servida por la API) sin Docker ni credenciales de Fly.io.

Este smoke **no es un deploy**: no se creó ninguna app en Fly.io ni se subió
ninguna imagen. El build de la imagen y el deploy real quedan como handoff.

## Preparación

```powershell
# 1. Build del frontend
cd frontend
pnpm build
cd ..

# 2. Publish del backend y copia de la SPA al wwwroot del publish
dotnet publish backend/FashionApp.Api -c Release -o .tmp/publish
Remove-Item .tmp/publish/wwwroot -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item frontend/dist .tmp/publish/wwwroot -Recurse

# 3. Arranque con la demo aislada (feeds fake, job apagado)
$root = (Resolve-Path .tmp/smoke).Path
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5099'
$env:DATA__ROOT = Join-Path $root 'data'
$env:DEMO__ENABLED = 'true'
$env:DEMO__DATAROOT = Join-Path $root 'demo'
$env:DIGEST__PROVIDER = 'fake'
$env:DIGEST__GENERATEONSTARTUP = 'false'

dotnet .tmp/publish/FashionApp.Api.dll
```

La app siembra 14 prendas y un digest de 8 notas en `.tmp/smoke/demo` antes de
empezar a escuchar. `DATA__ROOT` y `DEMO__DATAROOT` son absolutos y distintos, así
que el guard de `DataPaths` acepta la configuración.

## Resultados reales

| # | Pedido | Resultado observado |
|---|---|---|
| 1 | `GET /api/health` | `200` · `{"status":"ok","utc":"2026-11-09T03:24:20.2724436+00:00"}` |
| 2 | `GET /` | `200` · `text/html` · `Cache-Control: no-cache` · contiene `<title>Fashion App` |
| 3 | `GET /guardarropa` (fallback SPA) | `200` · `text/html` · `Cache-Control: no-cache` |
| 4 | `GET /assets/index-DVeyCoBa.js` | `200` · `Cache-Control: public, max-age=31536000, immutable` |
| 5 | `GET /favicon.svg` | `200` · `image/svg+xml` · `Cache-Control: public, max-age=3600` |
| 6 | `GET /api/config` | `200` · `{"demo":true}` |
| 7 | `GET /api/digest/today` | `200` · **8 ítems** · resumen de 73 caracteres |
| 8 | `GET /api/garments` | `200` · **14 prendas** sembradas |
| 9 | `GET /media/9bcde6c6…f0c.webp` | `200` · `image/webp` · `Cache-Control: public, max-age=31536000, immutable` · 7.598 bytes |
| 10 | `POST /api/digest/refresh` | `403` (demo de solo lectura) |

Comandos usados para la verificación (PowerShell):

```powershell
$base = 'http://127.0.0.1:5099'

Invoke-WebRequest -UseBasicParsing "$base/api/health"
Invoke-WebRequest -UseBasicParsing "$base/" |
  Select-Object StatusCode, @{ n = 'Content-Type'; e = { $_.Headers['Content-Type'] } },
                @{ n = 'Cache-Control'; e = { $_.Headers['Cache-Control'] } }
Invoke-WebRequest -UseBasicParsing "$base/guardarropa"
Invoke-WebRequest -UseBasicParsing "$base/assets/index-DVeyCoBa.js"
Invoke-WebRequest -UseBasicParsing "$base/favicon.svg"
Invoke-RestMethod "$base/api/config"
Invoke-RestMethod "$base/api/digest/today" | Select-Object @{ n = 'Items'; e = { $_.items.Count } }
Invoke-RestMethod "$base/api/garments" | Select-Object @{ n = 'Items'; e = { $_.items.Count } }
Invoke-WebRequest -UseBasicParsing "$base/media/<hash>.webp"
try { Invoke-WebRequest -UseBasicParsing -Method Post "$base/api/digest/refresh" }
catch { $_.Exception.Response.StatusCode.value__ }
```

`index-DVeyCoBa.js` y el hash de `/media/…` son los del build de esta corrida;
los nombres con hash cambian en cada build. Lo estable es el `Cache-Control`.

## Lo que este smoke no cubre

- **Build de la imagen Docker.** No hay Docker en este entorno; el `Dockerfile`
  se valida en el handoff con `fly deploy`.
- **Deploy en Fly.io.** Sin `flyctl` ni credenciales, no se creó app ni volumen.
- **Proxy de Fly y TLS.** El smoke corre sobre `127.0.0.1`; la resolución de IP
  detrás de `Fly-Client-IP`/`X-Forwarded-For` está cubierta por tests, no por
  este smoke. La Machine debe quedar accesible solo por el proxy de Fly
  (ver [deploy.md](deploy.md)).
- **Lighthouse.** La medición de LCP está en [metrics/README.md](metrics/README.md);
  el LCP fuera de presupuesto queda aceptado como deuda en
  [perf-followup.md](perf-followup.md).

## Suites ejecutadas en el cierre

| Suite | Comando | Resultado |
|---|---|---|
| Backend | `dotnet test backend/FashionApp.sln` | **265** tests, 0 fallos (254 previos + 11 nuevos) |
| Frontend unit | `pnpm vitest run` | **51** tests, 0 fallos |
| Lint | `pnpm lint` | limpio |
| Build | `pnpm build` | OK, SPA en `frontend/dist` |
| E2E | `pnpm exec playwright test` | **11** pasaron, 2 specs de captura omitidos |
| Captura de flujo | `CAPTURE=1 pnpm exec playwright test tests/upload-flow.spec.ts` | **1** pasó; generó `upload-flow.webm` y `upload-flow-poster.png` |
