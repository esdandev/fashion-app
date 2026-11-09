# Métricas de Lighthouse

Medición de la portada de la **demo** servida por el build de producción local.
Los JSON crudos de las tres corridas están en este directorio; el README los
enlaza desde la sección **Rendimiento**.

## Entorno

| Dato | Valor |
|---|---|
| Fecha de las corridas | 2026-11-08 (fetch `2026-11-09T02:51Z`) |
| SO | Windows 11 Home 10.0.26300 (build 26300) |
| CPU | AMD Ryzen 5 8500G (6 núcleos / 12 hilos) |
| RAM | 23,1 GB |
| Node | v24.22.1 |
| pnpm | 12.18.0 |
| Lighthouse | 13.6.0 |
| Chromium | 158.0.8127.18 (Playwright `chromium-1254`) |
| App | build de producción local, `Production`, demo sembrada |
| URL | `http://localhost:8091/` |

## Build y servidor

```bash
# 1. Bundle de producción del frontend
cd frontend
pnpm build

# 2. Publicación de la API
cd ..
dotnet publish backend/FashionApp.Api -c Release -o .tmp/publish

# 3. La SPA se copia DESPUÉS del publish, dentro de su wwwroot
cp -r frontend/dist/* .tmp/publish/wwwroot/
```

El proceso se levanta desde `.tmp/publish` con:

```powershell
$env:ASPNETCORE_ENVIRONMENT='Production'
$env:ASPNETCORE_URLS='http://localhost:8091'
$env:DATA__ROOT='<temp>\fashion-prod-smoke\data'
$env:DEMO__ENABLED='true'
$env:DEMO__DATAROOT='<temp>\fashion-prod-smoke\data\demo'
$env:AI__PROVIDER='fake'
$env:DIGEST__PROVIDER='fake'
$env:DIGEST__GENERATEONSTARTUP='false'
dotnet FashionApp.Api.dll
```

`<temp>` fue un directorio bajo `%LOCALAPPDATA%\Temp`; la base y los uploads
son efímeros y no se versionan.

## Comando exacto

```powershell
$env:CHROME_PATH='C:\Users\danie\AppData\Local\ms-playwright\chromium-1254\chrome-win64\chrome.exe'
pnpm dlx lighthouse http://localhost:8091/ `
  --only-categories=performance `
  --output=json `
  --output-path=C:\Users\danie\Desktop\FashionApp\docs\metrics\lighthouse-mobile-1.json `
  --quiet `
  --chrome-flags="--headless --no-sandbox --disable-gpu"
```

Se repitió tres veces cambiando solo el nombre del archivo de salida a
`lighthouse-mobile-2.json` y `lighthouse-mobile-3.json`. Lighthouse usa su
**preset móvil por defecto**: red y CPU simuladas (Lantern), viewport móvil y
las mismas flags de Chromium en las tres corridas.

## Resultados

| Corrida | Performance | FCP | LCP | TBT | CLS | Speed Index |
|---|---|---|---|---|---|---|
| 1 | 93 | 903 ms | 3.140 ms | 55 ms | 0,060 | 1.617 ms |
| 2 | 93 | 960 ms | 3.223 ms | 53 ms | 0,060 | 1.555 ms |
| 3 | 93 | 960 ms | 3.212 ms | 42 ms | 0,060 | 1.585 ms |
| **Mediana** | **93** | **960 ms** | **3.212 ms** | **53 ms** | **0,060** | **1.585 ms** |

- **INP no se midió.** Es una métrica de campo que requiere interacción real;
  TBT es el proxy de laboratorio y no se debe leer como INP.
- El presupuesto de LCP (< 2,5 s) **no se cumple** en este entorno.
- Los tres JSON identifican el mismo elemento LCP: el subtítulo de la portada
  (`main > section > div > p.mt-1`, «Lo que pasa en la moda…»).
- El *breakdown* observado de esas corridas fue TTFB 9–21 ms y render delay
  433–460 ms. Esos valores no explican por sí solos el LCP simulado de
  3,1–3,2 s: la atribución de la causa es una hipótesis, no un resultado
  medido. El seguimiento está en
  [../perf-followup.md](../perf-followup.md).

## Reproducir

1. Repetir los pasos de **Build y servidor**.
2. Correr el comando exacto tres veces.
3. Comparar la mediana de los tres JSON; no mezclar corridas de distinta
   máquina, flags o versión de Lighthouse.
