# Fashion App

App web personal de moda: subí la foto de una prenda, la IA la cataloga y el
guardarropa se convierte en la base para armar outfits y leer un resumen diario
de noticias relacionado con lo que ya tenés.

Proyecto de portfolio: un solo dueño, un solo contenedor, sin cuentas. La API
.NET sirve la SPA compilada y guarda todo en SQLite + archivos dentro de un
único volumen persistente.

## Capturas

Las imágenes se generan con el spec de Playwright
(`frontend/tests/screenshots.spec.ts`) sobre datos sembrados por API: 14 prendas,
un digest de 8 notas y tres outfits.

| Hoy | Guardarropa | Outfits |
|---|---|---|
| ![Hoy en desktop](docs/screenshots/hoy-desktop.png) | ![Guardarropa en desktop](docs/screenshots/guardarropa-desktop.png) | ![Outfits en desktop](docs/screenshots/outfits-desktop.png) |

| Hoy (mobile) | Guardarropa (mobile) | Outfits (mobile) |
|---|---|---|
| <img src="docs/screenshots/hoy-mobile.png" alt="Hoy en mobile" width="260"> | <img src="docs/screenshots/guardarropa-mobile.png" alt="Guardarropa en mobile" width="260"> | <img src="docs/screenshots/outfits-mobile.png" alt="Outfits en mobile" width="260"> |

> Las fotos de las prendas son assets de demo genéricos, generados
> programáticamente. Antes de publicar el portfolio conviene reemplazarlos por
> fotos con licencia (ver **Límites conocidos**).

### Flujo de alta en video

[![Formulario de confirmación del alta](docs/screenshots/upload-flow-poster.png)](docs/screenshots/upload-flow.webm)

Video WebM del flujo completo **subir foto → analizar → confirmar**, grabado con
el backend aislado y el analizador fake. Se regenera solo a pedido, igual que
las capturas:

```bash
cd frontend
CAPTURE=1 pnpm exec playwright test tests/upload-flow.spec.ts
```

```powershell
# PowerShell
cd frontend
$env:CAPTURE='1'
pnpm exec playwright test tests/upload-flow.spec.ts
```

El spec está apagado salvo `CAPTURE=1` y **no corre en CI**: es una grabación de
portfolio, no un test de regresión.

## Features

- **Alta asistida por IA.** La foto se normaliza a WebP (EXIF eliminado,
  miniatura de 400 px) y se analiza para proponer nombre, categoría, color,
  temporada, formalidad y etiquetas de estilo. El alta va en dos pasos
  (analizar → confirmar), así cerrar el diálogo nunca deja una fila huérfana.
- **Guardarropa filtrable y editable.** Grilla responsive con filtros por categoría, color
  y temporada, paginación acumulativa y muestra del color real de cada prenda.
  Cada prenda se puede eliminar con confirmación; los outfits guardados conservan su foto.
- **Outfits con prendas reales.** Sugerencias por ocasión y clima, collage vertical
  tipo maniquí con las fotos, guardado con la razón de la propuesta, edición manual
  (nombre, ocasión, favorito y composición) y eliminación con confirmación.
- **Portada Hoy.** Digest diario generado desde feeds RSS, con resumen propio,
  etiquetas de estilo, links a las fuentes y las prendas del guardarropa que
  combinan con cada noticia. La respuesta usa `ETag`/`304` y caché privada.
- **Demo pública de solo lectura.** Datos sembrados, IA y feeds fake, costo
  cero y todas las escrituras responden `403`.
- **Límites y abuso controlados.** Cuotas diarias de IA, rate limit por IP y
  errores `ProblemDetails` en español.
- **Privacidad por diseño.** El token es un secreto compartido, las imágenes se
  envían al proveedor de IA solo al analizarlas y la portada no expone prendas
  a requests sin token cuando la instancia personal lo exige.

## Stack

| Capa | Tecnología |
|---|---|
| Backend | .NET 10, Minimal APIs, EF Core 10, SQLite |
| Frontend | Vite 8, React 19, TypeScript, Tailwind 4, TanStack Query 5, React Router 7 |
| Imágenes | Magick.NET (WebP, EXIF eliminado, miniaturas de 400 px) |
| IA | Gemini detrás de `IGarmentAnalyzer` / `IOutfitSuggester`; implementaciones `fake` para desarrollo, tests y demo |
| Noticias | Feeds RSS/Atom + `BackgroundService` diario; resumen con Gemini o fake |
| Infra | Docker multi-stage, Fly.io, volumen `/data`, GitHub Actions |
| Tests | xUnit + `WebApplicationFactory`, Vitest + Testing Library, Playwright, Lighthouse |

## Arquitectura

```mermaid
flowchart LR
  SPA["React 19 · Vite"] -->|"SPA y /api/*"| API["ASP.NET Core 10"]
  SPA -->|"/media/*"| Media["WebP + miniaturas"]
  API --> DB[("SQLite")]
  API --> Uploads[("uploads/")]
  Job["Job diario de RSS + IA"] --> DB
  Media --> Uploads
```

- **Un solo origen en producción:** la API sirve `index.html`, los assets con
  hash y `/api/*` + `/media/*` desde el mismo puerto (`8080` en el contenedor).
- **Una sola raíz de datos:** `DATA__ROOT` deriva `fashion.db` y `uploads/`. La
  demo usa una raíz propia (`DATA__ROOT/demo`) y el arranque falla si intenta
  apuntar a la base o a los uploads personales.
- **La IA propone, el backend valida:** ids, URLs, etiquetas y composiciones se
  revalidan antes de persistir. Los proveedores fake hacen deterministas el CI
  y la demo.

Diagrama completo, decisiones y topología de deploy:
[docs/architecture.md](docs/architecture.md).

## Cómo correr local

Requisitos:

- **.NET 10 SDK** (`dotnet --version`)
- **Node 22+** y **pnpm** (`node --version`, `pnpm --version`)
- Chromium de Playwright, solo para el E2E:
  `pnpm exec playwright install chromium`

### 1. Backend

Desde la raíz del repositorio:

```powershell
# PowerShell
$env:Ai__Provider='fake'
dotnet run --project backend/FashionApp.Api
```

```bash
# Linux/macOS
Ai__Provider=fake dotnet run --project backend/FashionApp.Api
```

- API: `http://localhost:5210` (definido en
  `backend/FashionApp.Api/appsettings.Development.json`).
- Health check: `GET http://localhost:5210/api/health`.
- Datos: `data/fashion.db` y `data/uploads/` (fuera de git). Cambiá la raíz con
  `DATA__ROOT`.
- `Ai__Provider=fake` devuelve una camisa de lino determinística, sin red ni
  costo. Para usar Gemini, definí las variables **en la misma terminal** y
  antes de arrancar:

```powershell
$env:AI__PROVIDER='gemini'
$env:AI__APIKEY='tu-clave'
$env:AI__MODEL='gemini-3.8-flash'   # opcional; ya es el default
dotnet run --project backend/FashionApp.Api
```

  La clave sola no activa Gemini: si `AI__PROVIDER` queda en `fake`, la app
  sigue respondiendo datos de ejemplo. La app lee automáticamente un archivo
  `.env` en la raíz del repo (o hasta cinco carpetas arriba) al arrancar; las
  variables reales del entorno tienen prioridad. La forma más simple:

```powershell
Copy-Item .env.example .env
# editá .env con AI__PROVIDER=opencode y AI__APIKEY=...
dotnet run --project backend/FashionApp.Api
```
- Verificá la configuración activa con `GET /api/ai/status` (si definiste
  `APP__TOKEN`, mandá `X-App-Token`). Debe responder `provider: "gemini"` y
  `keyConfigured: true`.
- Si la IA devuelve confianza 0 o el log muestra un error de Gemini, probá la
  clave y el modelo fuera de la app:

```powershell
.\scripts\check-gemini.ps1
.\scripts\check-gemini.ps1 -ListModels
```

  El modelo `gemini-2.0-flash` está retirado desde junio de 2026. El default es
  `gemini-3.8-flash`; si tu cuenta no lo tiene, elegí otro de `-ListModels` y
  definí `AI__MODEL`.
- **OpenCode como proveedor.** OpenCode Console expone una API de inferencia en
  `https://opencode.ai/inference`, con endpoints compatibles con Gemini, OpenAI
  y Anthropic. La app usa el endpoint Gemini-compatible, así que el análisis de
  imágenes funciona con modelos Gemini:

```powershell
$env:AI__PROVIDER='opencode'
$env:AI__APIKEY='<personal-api-key-de-opencode>'
$env:AI__MODEL='gemini-4-argon'   # opcional; ya es el default de OpenCode
dotnet run --project backend/FashionApp.Api
```

  La clave es una **API key personal de OpenCode Console**, no la sesión del
  agente. Los modelos free solo funcionan dentro de OpenCode y devuelven `403`
  desde otros clientes; usá un modelo pago o una suscripción Go. `AI__BASEURL`
  permite apuntar a otro gateway compatible. Verificá con
  `.\scripts\check-opencode.ps1` o `.\scripts\check-opencode.ps1 -ListModels`.
  Si devuelve `Account.InsufficientFunds`, la clave es válida pero la cuenta no
  tiene saldo/Go para ese modelo: cargá crédito en OpenCode Console o cambiá a
  `AI__PROVIDER=gemini` con una clave de Google AI Studio.
- **DeepSeek V4.1 Flash.** Es multimodal y usa OpenAI Chat Completions; la app
  detecta la familia automáticamente por el nombre del modelo:

```env
AI__PROVIDER=opencode
AI__APIKEY='<personal-api-key-de-opencode>'
AI__MODEL=deepseek-v4.1-flash
```

  `GET /api/ai/status` debe mostrar `"apiFamily":"OpenAi"`. Es un modelo pago
  ($0.30/$1.20 por 1M de tokens); si devuelve `Account.InsufficientFunds`,
  cargá saldo/Go en Console. `deepseek-v4.1-flash-free` solo funciona dentro de
  OpenCode, no desde la app. Verificá con:

```powershell
.\scripts\check-opencode.ps1 -Family openai -Model deepseek-v4.1-flash
```
- En `Development`, `Digest__Provider=fake` devuelve ocho notas de ejemplo;
  para feeds reales usá `DIGEST__PROVIDER=rss` y `DIGEST__FEEDS__...`.

### 2. Frontend

En otra terminal:

```bash
cd frontend
pnpm install
pnpm dev
```

- App: `http://localhost:5173`.
- Por defecto consume `http://localhost:5210`; para apuntar a otro backend:
  `VITE_API_URL=http://localhost:5210 pnpm dev`.

### 3. Modo demo

La demo siembra 14 prendas y un digest de 8 notas, usa una raíz propia y no
acepta escrituras:

```powershell
$env:DEMO__ENABLED='true'
dotnet run --project backend/FashionApp.Api
```

También se puede levantar todo con Docker: `docker compose up --build`
(app en `http://localhost:8080`). Ver [docs/deploy.md](docs/deploy.md).

## Tests y verificación

```bash
# Backend
dotnet test backend/FashionApp.sln

# Frontend: unitarios
cd frontend
pnpm vitest run

# Frontend: typecheck + build de producción
pnpm build

# Frontend: lint
pnpm lint

# E2E (levanta API aislada + Vite automáticamente)
pnpm exec playwright test
```

Playwright usa `.test-data` (SQLite + uploads) y lo borra al inicio de cada
run; nunca toca `data/`. Antes de correrlo, **detené cualquier backend o Vite
propio en los puertos 5210/5173**: el runner no reutiliza servidores existentes
(`reuseExistingServer: false`) para no mutar datos reales. La API de test corre
con IA y feeds fake, sin token y con `Demo__Enabled=false`. Los specs cubren:

- **Guardarropa:** subir foto, confirmar, editar, ver la prenda, spinner,
  reanálisis, cancelación, eliminación, rechazo de archivos > 10 MB y demo de solo lectura.
- **Outfits:** sugerir, guardar con `source=ai`, crear a mano, renombrar y eliminar.
- **Hoy:** sembrar una prenda, regenerar el digest con `X-Admin-Secret` y
  verificar el hero, el link externo y la miniatura relacionada.

**Capturas de portfolio** (no corren en CI; `CAPTURE=1` las habilita):

```bash
cd frontend
CAPTURE=1 pnpm exec playwright test tests/screenshots.spec.ts
```

```powershell
# PowerShell
cd frontend
$env:CAPTURE='1'
pnpm exec playwright test tests/screenshots.spec.ts
```

Escribe seis PNG en `docs/screenshots/`: viewport desktop 1440×900 y mobile
390×844, con el PNG mobile a 2× (780×1688). La portada **Hoy** usa un viewport
más alto —1440×1100 desktop y 390×1050 mobile (PNG 780×2100)— para que se vea
completa la tira «En tu guardarropa»; el resto de las capturas conserva el
viewport estándar. Para que las notas no queden con bloques vacíos, el spec
intercepta `GET /api/digest/today` **solo durante la captura de Hoy** y asigna
`imageUrl` a las primeras tres notas usando imágenes demo de
`frontend/public/`; la API y el código de producción no cambian. Sin
`CAPTURE=1` el spec se salta.

## Variables de entorno

Las variables de .NET usan `Sección__Clave`. Las del front deben empezar con
`VITE_`.

| Variable | Default | Descripción |
|---|---|---|
| `DATA__ROOT` | `../../data` | Raíz única de datos; de ella derivan SQLite y uploads. |
| `APP__TOKEN` | vacío | Token estático de la instancia personal. Obligatorio en `Production` (salvo `APP__ALLOWANONYMOUS=true`). |
| `APP__ALLOWANONYMOUS` | `false` | Escape hatch explícito para una instancia personal sin token en `Production`. |
| `DEMO__ENABLED` | `false` | Modo demo: siembra datos, usa su propia raíz y rechaza escrituras con `403`. |
| `DEMO__DATAROOT` | `<DATA__ROOT>/demo` | Raíz de datos de la demo. No puede coincidir con la base ni los uploads personales. |
| `AI__PROVIDER` | `fake` | `fake`, `gemini` u `opencode`. |
| `AI__APIKEY` | vacío | Obligatoria cuando `AI__PROVIDER` es `gemini` u `opencode`. |
| `AI__MODEL` | según proveedor | `gemini-3.8-flash` con Gemini; `gemini-4-argon` con OpenCode. Podés overridearlo. |
| `AI__BASEURL` | vacío | Override del base URL del proveedor. OpenCode usa `https://opencode.ai/inference`. |
| `AI__DAILYANALYSISLIMIT` | `50` | Análisis de IA por día (calendario UTC). |
| `AI__DAILYSUGGESTIONLIMIT` | `20` | Sugerencias de outfits por día. Comparte el contador atómico en SQLite. |
| `DIGEST__PROVIDER` | `rss` (`fake` en `Development`) | `rss` lee feeds reales; `fake` devuelve notas determinísticas sin red. |
| `DIGEST__FEEDS` | `[]` | Lista de `{ "Name", "Url" }` de feeds RSS/Atom. |
| `DIGEST__RUNAT` | `07:00` | Hora local del servidor del job diario. |
| `DIGEST__GENERATEONSTARTUP` | `true` | Genera el digest al arrancar si falta el de hoy. |
| `DIGEST__MAXITEMS` | `10` | Máximo de notas persistidas por digest. |
| `DIGEST__MAXAGEHOURS` | `48` | Descarta notas publicadas hace más de estas horas. |
| `DIGEST__ADMINSECRET` | vacío | Secreto de `POST /api/digest/refresh` (`X-Admin-Secret`). Vacío lo deshabilita. |
| `RATELIMITING__ENABLED` | `true` | Rate limit por IP en `/api/garments`, `/api/outfits/suggest` y `/api/digest/refresh`. |
| `RATELIMITING__PERMITLIMIT` | `60` | Solicitudes por minuto y por IP; el exceso responde `429` + `Retry-After`. Detrás del proxy usa `Fly-Client-IP` y luego el primer `X-Forwarded-For`. |
| `CONNECTIONSTRINGS__DEFAULT` | derivada de `DATA__ROOT` | Override de SQLite para CI/tests. |
| `STORAGE__ROOT` | `<DATA__ROOT>/uploads` | Override de la carpeta de imágenes. |
| `CORS__ALLOWEDORIGIN` | `http://localhost:5173` | Único origen permitido por CORS en desarrollo. |
| `VITE_API_URL` | `http://localhost:5210` | URL de la API usada en build/dev del front (`/` dentro del contenedor). |
| `ASPNETCORE_ENVIRONMENT` | `Production` (framework) | `Development` habilita datos locales; el perfil `dotnet run` lo setea solo. `Production` activa las validaciones de seguridad del contenedor. |

`APP__TOKEN` no es autenticación real: es un secreto compartido para una
instancia personal y así queda documentado.

## Deploy

La app se publica como **un solo contenedor** en Fly.io (región `gru`, puerto
`8080`, volumen `fashion_data` en `/data`, health check `GET /api/health`).

```bash
fly auth login
fly apps create fashion-app-demo
fly volumes create fashion_data --region gru --size 1
fly deploy
fly open
```

La demo corre con IA y feeds fake, solo lectura. La instancia personal se
activa con secretos (`APP__TOKEN`, `AI__PROVIDER=gemini`, `AI__APIKEY`,
`DEMO__ENABLED=false`).

Comandos completos, verificación, reset de demo y límites:
**[docs/deploy.md](docs/deploy.md)**.

> **Handoff:** no hay `flyctl` ni credenciales de Fly.io en este entorno, así que
> **nada se deployó desde acá**. El build de la imagen, la creación de la app y
> el volumen quedan a cargo del dueño de la cuenta.
>
> **Proxy de Fly:** el rate limit usa `Fly-Client-IP` y, si falta, el primer
> `X-Forwarded-For`, antes de caer a la dirección del socket. La Machine debe
> quedar accesible **solo** a través del proxy: una conexión directa permitiría
> falsificar esos headers y saltear el límite por IP.

## Rendimiento

Presupuesto del diseño: **LCP < 2,5 s · INP < 200 ms · CLS < 0,1** en p75
móvil; lecturas de API p95 < 300 ms.

### Bundle del build de producción

Medido con `pnpm build` (Vite 8, React 19). Los nombres llevan hash y cambian
en cada build.

| Recurso | Tamaño | Gzip |
|---|---|---|
| JS de entrada (`index-<hash>.js`) | 287,73 KB | 91,05 KB |
| CSS (`index-<hash>.css`) | 32,33 KB | 6,55 KB |
| Chunk de `/hoy` (`TodayPage-<hash>.js`) | 7,54 KB | 2,43 KB |
| Cliente API compartido (`api-<hash>.js`) | 10,81 KB | 3,85 KB |
| Chunk de `/guardarropa` | 25,45 KB | 7,26 KB |
| Chunk de `/outfits` | 32,78 KB | 8,27 KB |
| Labels compartidos (`garmentLabels-<hash>.js`) | 1,26 KB | 0,49 KB |
| `index.html` | 0,65 KB | 0,38 KB |

La primera carga de `/` transfiere el JS de entrada + el chunk de Hoy + el
cliente API: unos **97,3 KB gzip** de JavaScript y 6,55 KB gzip de CSS. El
guardarropa y los outfits se cargan bajo demanda.

### Lighthouse móvil

Medición local sobre el build de producción real: `pnpm build`,
`dotnet publish -c Release` y **después** copia de `dist/` al `wwwroot` del
publish; la app publicada corre en `Production` con la demo sembrada.
Lighthouse 13.6.0, preset móvil por defecto (red y CPU simuladas), tres
corridas sobre `/`. El comando exacto, el hardware y los flags están en
[docs/metrics/README.md](docs/metrics/README.md); los JSON crudos de las tres
corridas quedan versionados en `docs/metrics/`.

| Métrica | Mediana | Rango de las 3 corridas | Presupuesto |
|---|---|---|---|
| Performance score | 93 | 93–93 | — |
| LCP | 3,21 s | 3,14–3,22 s | < 2,5 s |
| TBT (proxy de laboratorio de INP) | 53 ms | 42–55 ms | — |
| CLS | 0,060 | 0,060–0,060 | < 0,1 |
| Speed Index | 1,59 s | 1,56–1,62 s | — |
| FCP | 0,96 s | 0,90–0,96 s | — |

- **INP no se midió**: es una métrica de campo (requiere interacción real). En
  laboratorio se reporta **TBT** como proxy; no se inventa un valor de INP.
- **El LCP quedó por encima del presupuesto de 2,5 s** en este entorno. Los
  JSON de Lighthouse identifican el subtítulo de la portada como elemento LCP
  en las tres corridas. El *breakdown* observado (TTFB 9–21 ms y render delay
  433–460 ms) no explica por sí solo el LCP simulado de 3,1–3,2 s: la causa
  (hero renderizado por el cliente después de la consulta del digest / trabajo
  de JS bajo throttling simulado) queda como **hipótesis a validar**, no como
  un hecho medido.
- El **CLS** dio 0,060 en las tres corridas de esta medición.
- Los números dependen de hardware, red y carga de la máquina. No son una
  medición de campo ni de usuarios reales.

### Seguimiento

El LCP fuera de presupuesto tiene una tarea de seguimiento abierta en
[docs/perf-followup.md](docs/perf-followup.md): precargar/renderizar el hero de
Hoy antes y estabilizar su altura, con una medición antes/después usando el
mismo comando de Lighthouse. No se optimizó código de performance en la Fase 4.

## Límites conocidos

- **Deploy no ejecutado.** No hay `flyctl` ni credenciales en este entorno. La
  configuración, el Dockerfile y el handoff están listos; `docs/deploy.md`
  documenta qué se validó y qué no.
- **Imagen Docker no construida acá.** No hay Docker disponible. Sí se validó
  `pnpm build`, `dotnet publish` y un smoke de producción local con la SPA en
  `wwwroot`, `/api/health`, `/api/digest/today`, `/media/*` y el fallback de
  rutas. Comandos y resultados reales en [docs/smoke.md](docs/smoke.md).
- **Fotos de demo genéricas.** Los assets del manifiesto son placeholders
  generados con Magick.NET y llevan la palabra «Demo» impresa. Reemplazarlos
  por fotos con licencia antes de un portfolio público.
- **Demo con IA fake.** La demo pública no representa la calidad del análisis
  real: usa el analizador y los feeds determinísticos para tener costo cero.
- **Token, no autenticación.** `APP__TOKEN` es un secreto compartido de un solo
  usuario; no hay cuentas, sesiones ni permisos por usuario.
- **SQLite y una sola Machine.** No hay escritura concurrente multi-nodo ni
  escalado horizontal; el volumen vive en una región y el deploy reinicia la
  Machine.
- **Métricas de laboratorio y LCP fuera de presupuesto (aceptado).** INP no
  está medido y el LCP local (3,21 s) supera el presupuesto de 2,5 s. Lighthouse
  no reemplaza una medición de campo. La Fase 4 cierra con esa deuda
  **explícitamente aceptada** y el seguimiento accionable queda en
  [docs/perf-followup.md](docs/perf-followup.md); no se reescribió el hero en
  esta ola.
- **CI sin remoto acá.** El workflow de GitHub Actions está versionado, pero
  este clon no tiene remoto configurado: los jobs corren cuando el repo se
  publique en GitHub.

## Estructura del repositorio

```
.
├── backend/
│   ├── FashionApp.Api/          API .NET, dominio, features, infraestructura
│   │   ├── Domain/              Garment, Outfit, Digest, cuotas
│   │   ├── Features/            endpoints de garments, outfits, digest y security
│   │   └── Infrastructure/      EF Core, storage, IA, feeds, demo, rate limit
│   └── FashionApp.Tests/        xUnit + WebApplicationFactory
├── frontend/
│   ├── src/                     React 19 + Vite
│   │   ├── features/            digest (Hoy), wardrobe, outfits
│   │   └── lib/                 cliente API y tipos
│   └── tests/                   Playwright E2E + capturas de portfolio
├── docs/
│   ├── architecture.md          diagramas y decisiones técnicas
│   ├── deploy.md                handoff de Fly.io
│   ├── metrics/                 JSON de Lighthouse y entorno de medición
│   ├── smoke.md                 smoke de producción local (resultados reales)
│   ├── perf-followup.md         deuda aceptada de performance del hero de Hoy
│   ├── plan-fase-*.md           planes de implementación
│   ├── Fashion App.md           spec y bitácora
│   └── screenshots/             capturas desktop y mobile
├── .github/workflows/ci.yml     backend + frontend + e2e
├── Dockerfile                   imagen multi-stage (Node + .NET)
├── docker-compose.yml           demo local con volumen
├── fly.toml                     demo pública en Fly.io
└── README.md
```

## API

<details>
<summary>Endpoints principales</summary>

| Método | Ruta | Función |
|---|---|---|
| GET | `/api/health` | Estado del servicio y hora UTC. |
| GET | `/api/config` | Flags públicos de runtime (`demo`). |
| GET | `/api/digest/today` | Último digest disponible con `ETag`/`304`; `404` si no hay ninguno. |
| POST | `/api/digest/refresh` | Regenera el digest de hoy con `X-Admin-Secret`, cooldown de 1 h y bloqueo en demo. |
| GET | `/api/garments` | Listado paginado con filtros `category`, `color`, `season`, `cursor`. |
| POST | `/api/garments/analyze` | Multipart: normaliza, guarda la imagen y analiza **sin** crear la prenda. |
| POST | `/api/garments/confirm` | Crea la prenda con los valores confirmados; deduplica por hash. |
| PATCH | `/api/garments/{id}` | Corrige nombre, categoría, color, formalidad, temporadas y etiquetas. |
| DELETE | `/api/garments/{id}` | Archiva la prenda (baja lógica). |
| POST | `/api/garments/{id}/reanalyze` | Reanaliza y devuelve una propuesta sin sobrescribir. |
| GET | `/api/outfits` | Outfits con sus prendas, favoritos primero. |
| POST | `/api/outfits` | Crea un outfit manual o guarda una sugerencia (`source=ai`). |
| PATCH | `/api/outfits/{id}` | Edita nombre, ocasión, favorito o reemplaza las prendas. |
| DELETE | `/api/outfits/{id}` | Elimina el outfit y sus ítems. |
| POST | `/api/outfits/suggest` | Sugerencias por `occasion` y `weather`; no persiste. |
| GET | `/media/{hash}.webp` | Imagen normalizada con caché inmutable. |
| GET | `/media/{hash}-thumb.webp` | Miniatura de 400 px. |

Los errores usan `ProblemDetails` (RFC 7807) en español. El alta tiene un
endpoint de compatibilidad (`POST /api/garments`) que analiza y persiste en un
request, pero la UI usa `analyze` + `confirm`.

</details>

## Documentación

- Spec y bitácora: [docs/Fashion App.md](docs/Fashion%20App.md)
- Arquitectura: [docs/architecture.md](docs/architecture.md)
- Deploy en Fly.io: [docs/deploy.md](docs/deploy.md)
- Smoke de producción local: [docs/smoke.md](docs/smoke.md)
- Métricas de Lighthouse: [docs/metrics/README.md](docs/metrics/README.md)
- Seguimiento de performance: [docs/perf-followup.md](docs/perf-followup.md)
- Plan de la Fase 4: [docs/plan-fase-4.md](docs/plan-fase-4.md)
