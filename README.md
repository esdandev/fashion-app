# Fashion App

App web personal de moda: guardarropa, outfits y resumen diario.

La **Fase 0–1** quedó cerrada: se sube una foto, se normaliza y analiza con IA,
se confirman o corrigen los atributos y la prenda aparece en la grilla del
guardarropa con filtros por categoría y temporada.

- Diseño: `docs/Fashion App.md`
- Plan de la Fase 0–1: `docs/plan-fase-0-1.md`
- Bitácora: `docs/Fashion App.md` → sección **Bitácora**

## Stack

| Capa | Tecnología |
|---|---|
| Backend | .NET 10, Minimal APIs, EF Core 10, SQLite |
| Frontend | Vite 8, React 19, TypeScript, Tailwind 4, TanStack Query 5, React Router 7 |
| Imágenes | Magick.NET (normalización a WebP, EXIF eliminado, miniaturas de 400 px) |
| IA | Gemini detrás de `IGarmentAnalyzer`; `fake` para desarrollo y tests |
| Tests | xUnit + `WebApplicationFactory`, Vitest, Playwright |

## Requisitos

- **.NET 10 SDK** (`dotnet --version`)
- **Node 22+** y **pnpm** (`node --version`, `pnpm --version`)
- Navegador de Playwright para el E2E (solo la primera vez):
  `pnpm exec playwright install chromium`
- No hace falta instalar SQLite por separado: EF Core incluye el proveedor.

## Cómo correr

### 1. Backend

Desde la raíz del repositorio:

```bash
# Linux/macOS
Ai__Provider=fake dotnet run --project backend/FashionApp.Api
```

```powershell
# PowerShell
$env:Ai__Provider='fake'
dotnet run --project backend/FashionApp.Api
```

- API: `http://localhost:5210` (definido en
  `backend/FashionApp.Api/appsettings.Development.json`).
- Health check: `GET http://localhost:5210/api/health`.
- Base SQLite: `data/fashion.db` (se crea sola y las migraciones corren al
  arrancar).
- Imágenes: `data/uploads`. Ambas rutas están fuera de git.

`Ai:Provider=fake` devuelve siempre una camisa de lino determinística, sin
llamadas de red ni costo. Para usar Gemini, definí `AI__PROVIDER=gemini` y
`AI__APIKEY` (el arranque falla si falta la clave).

### 2. Frontend

En otra terminal:

```bash
cd frontend
pnpm install
pnpm dev
```

- App: `http://localhost:5173`.
- Por defecto consume `http://localhost:5210`; para apuntar a otro backend:

```bash
VITE_API_URL=http://localhost:5210 pnpm dev
```

En la instancia personal, guardá el token una vez en `sessionStorage` con la
clave `app_token`; el cliente lo envía como `X-App-Token`.

## Tests y verificación

```bash
# Backend
dotnet test backend/FashionApp.sln

# Frontend: unitarios (Vitest)
cd frontend
pnpm test

# Frontend: typecheck + build de producción
pnpm build

# Frontend: lint
pnpm lint

# E2E (levanta backend aislado + Vite automáticamente)
pnpm exec playwright test
```

Playwright levanta **ambos** servidores (`webServer` en
`frontend/playwright.config.ts`): Vite en `5173` y la API en `5210`. La API de
test usa `frontend/../.test-data` (SQLite + uploads) y ese directorio se borra al
inicio de cada run, así que el E2E nunca toca la base de desarrollo `data/`.
Antes de correrlo, detené cualquier backend o Vite propio en esos puertos: el
runner no reutiliza servidores existentes para no mutar datos reales.

El backend de test arranca con `Ai__Provider=fake`, `Demo__Enabled=false`,
`App__Token` vacío y rutas de almacenamiento propias. El E2E cubre el flujo
crítico: subir foto → ver la confirmación → editar color/nombre → guardar → ver
la prenda en la grilla, además del spinner, el reanálisis sin guardar, la
cancelación sin fila nueva, el rechazo de archivos de más de 10 MB y el modo
demo de solo lectura.

## Variables de entorno

Las variables de .NET siguen el formato `Sección__Clave`. Las del front deben
empezar con `VITE_`.

| Variable | Default | Descripción |
|---|---|---|
| `APP__TOKEN` | vacío | Token estático de la instancia personal. Vacío = sin token (desarrollo). |
| `DEMO__ENABLED` | `false` | Modo demo: siembra datos y rechaza todas las escrituras con `403`. |
| `AI__PROVIDER` | `fake` | `fake` o `gemini`. |
| `AI__APIKEY` | vacío | Obligatoria cuando `AI__PROVIDER=gemini`. |
| `AI__MODEL` | `gemini-2.0-flash` | Modelo usado por el analizador real. |
| `AI__DAILYANALYSISLIMIT` | `50` | Análisis de IA permitidos por día (calendario UTC). |
| `CONNECTIONSTRINGS__DEFAULT` | `Data Source=../../data/fashion.db` | Ruta de SQLite, relativa al proyecto de la API. |
| `STORAGE__ROOT` | `data/uploads` | Originales y miniaturas normalizadas. |
| `CORS__ALLOWEDORIGIN` | `http://localhost:5173` | Único origen permitido por CORS. |
| `VITE_API_URL` | `http://localhost:5210` | URL de la API que usa el front (solo build/dev del front). |

`App:Token` no es autenticación real: es un secreto compartido para una
instancia personal y así queda documentado.

## Demo

```bash
# Linux/macOS
DEMO__ENABLED=true dotnet run --project backend/FashionApp.Api
```

```powershell
# PowerShell
$env:DEMO__ENABLED='true'
dotnet run --project backend/FashionApp.Api
```

- Siembra 14 prendas de ejemplo desde
  `backend/FashionApp.Api/Infrastructure/Demo/SeedAssets/manifest.json`.
- Solo siembra si la tabla de prendas está vacía: reiniciar no duplica nada.
- En demo, la UI y el backend son de solo lectura (las escrituras devuelven
  `403`) y no se llama a la IA.
- Para volver al estado inicial: detené el backend, borrá `data/fashion.db` y
  `data/uploads`, y arrancá de nuevo.

## Endpoints implementados

| Método | Ruta | Función |
|---|---|---|
| GET | `/api/health` | Estado del servicio y hora UTC. |
| GET | `/api/config` | Flags públicos de runtime (`demo`). |
| GET | `/api/garments` | Listado paginado. Filtros: `category`, `color`, `season`, `cursor`. |
| POST | `/api/garments/analyze` | Multipart (`file`): normaliza, guarda la imagen content-addressed y devuelve `contentHash`, URLs y `analysis` **sin crear la prenda**. Consume cuota de IA. |
| POST | `/api/garments/confirm` | JSON con los valores confirmados: crea la prenda usando los archivos del hash. No llama a la IA ni consume cuota. Deduplica por hash (`200`). |
| POST | `/api/garments` | Alta multipart de compatibilidad (`file`): analiza y persiste en un solo request. El frontend ya no lo usa. |
| PATCH | `/api/garments/{id}` | Corrige nombre, categoría, color (`colorName`/`colorHex`), formalidad, temporadas y etiquetas. |
| DELETE | `/api/garments/{id}` | Archiva (baja lógica; no borra archivos). |
| POST | `/api/garments/{id}/reanalyze` | Vuelve a analizar y devuelve una propuesta **sin** sobrescribir la prenda. |
| GET | `/media/{hash}.webp` | Imagen normalizada, con caché inmutable. |
| GET | `/media/{hash}-thumb.webp` | Miniatura de 400 px. |

Los errores usan `ProblemDetails` (RFC 7807) con mensajes en español. Los
endpoints de digest y outfits todavía no están implementados (Fases 2–3).

## Decisiones de la Fase 0–1

- **Alta en dos pasos: analizar → confirmar.** `POST /api/garments/analyze`
  normaliza, guarda la imagen content-addressed y analiza sin crear la prenda.
  Cerrar el diálogo después del análisis deja solo archivos reutilizables, nunca
  una fila huérfana. `POST /api/garments/confirm` crea la prenda con los valores
  confirmados, sin IA ni consumo de cuota.
- **Compatibilidad del alta directa.** `POST /api/garments` sigue existiendo con
  su contrato anterior (analiza y persiste en un request), pero la UI usa el
  flujo analyze + confirm.
- **Reanálisis no destructivo.** `reanalyze` devuelve una propuesta; el front
  vuelve a llenar el formulario y solo persiste si el usuario guarda.
- **Edición explícita.** Si la foto ya existe, el formulario actualiza la prenda
  con `PATCH /api/garments/{id}`; si es nueva, confirma con
  `POST /api/garments/confirm`.
- **Imágenes content-addressed.** El nombre sale del hash del contenido:
  re-subir la misma foto no crea otra prenda ni gasta otra llamada de IA.
- **Color con muestra.** El formulario acepta `colorName` y `colorHex`; el hex se
  guarda en confirm/PATCH y la tarjeta lo usa para el círculo de color.
- **Demo de solo lectura.** `GET /api/config` expone `{ demo }`; la UI oculta las
  acciones de escritura y explica en español por qué no se puede subir.
- **Sin librería de UI.** Tailwind 4 y componentes propios; rutas con
  `React.lazy` para no cargar el guardarropa en la home.
- **IA fake en tests.** Ni los tests de backend ni el E2E llaman a un proveedor
  real.

## Rendimiento

Presupuesto del diseño: **LCP < 2,5 s · INP < 200 ms · CLS < 0,1** en p75 móvil;
lecturas de API p95 < 300 ms.

Estado medido del build de producción (`pnpm build`):

| Recurso | Tamaño gzip |
|---|---|
| JS inicial (`index-*.js`) | ~90,8 KB |
| CSS | ~5,5 KB |
| Chunk de `/guardarropa` | ~9,2 KB (bajo demanda) |

El JS inicial queda muy por debajo del presupuesto de 200 KB gzip.

Medición local de Lighthouse 13.6 (preset móvil por defecto, build de
producción servido con `pnpm preview` y backend fake):

| Métrica | Resultado | Presupuesto |
|---|---|---|
| Performance score | 99 | — |
| LCP | 2,13 s | < 2,5 s |
| CLS | 0,055 | < 0,1 |
| Total blocking time | 8 ms | — |
| Speed Index | 1,30 s | — |

Los números dependen del hardware y de la red local; se vuelven a medir en la
fase de portfolio junto con las capturas del README.

## Capturas

Pendientes para la fase de portfolio: home, guardarropa con filtros, diálogo de
alta y formulario de confirmación.
