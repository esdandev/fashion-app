# Fashion App

App web personal de moda: guardarropa, outfits y resumen diario.

La **Fase 0–2** quedó cerrada: se sube una foto, se normaliza y analiza con IA,
se confirman o corrigen los atributos y la prenda aparece en la grilla del
guardarropa con filtros por categoría, color y temporada. Sobre ese guardarropa
se sugieren outfits por ocasión y clima, se guardan como collage y también se
arman o editan a mano.

- Diseño: `docs/Fashion App.md`
- Plan de la Fase 0–1: `docs/plan-fase-0-1.md`
- Plan de la Fase 2: `docs/plan-fase-2.md`
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
- Raíz de datos: `data/` (se crea sola), resuelta una única vez al arrancar.
  La base vive en `data/fashion.db` y las imágenes en `data/uploads`.
  Ambas rutas están fuera de git. Configurá otra raíz con `DATA__ROOT`.
- La demo usa su propia raíz (`data/demo` por defecto) para no leer ni borrar
  nunca la base personal.

`Ai:Provider=fake` devuelve siempre una camisa de lino determinística, sin
llamadas de red ni costo. Para usar Gemini, definí `AI__PROVIDER=gemini` y
`AI__APIKEY` (el arranque falla si falta la clave). En `Production`, arrancar la
instancia personal con `Ai:Provider=fake` deja un warning en el log: usaría
datos de ejemplo para fotos reales.

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

En `Production`, la instancia personal **no arranca** sin `App:Token` (salvo
`App:AllowAnonymous=true`, pensado solo para entornos descartables). En
`Development` y `Testing` el token puede quedar vacío para trabajar localmente.

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
`App__Token` vacío y rutas de almacenamiento propias. El E2E cubre dos flujos
críticos. **Guardarropa:** subir foto → ver la confirmación → editar color/nombre
→ guardar → ver la prenda en la grilla, además del spinner, el reanálisis sin
guardar, la cancelación sin fila nueva, el rechazo de archivos de más de 10 MB y
el modo demo de solo lectura. **Outfits:** un helper siembra tres prendas
(análisis multipart + confirmación JSON) para no depender del diálogo de alta,
luego sugiere un outfit, lo guarda y verifica la tarjeta con `source=ai`; el
segundo test crea un outfit a mano, edita el nombre y comprueba el cambio. Los
fixtures de outfits (`camisa-lino.webp`, `pantalon-beige.webp`,
`zapatillas-blancas.webp`) son copias de los assets de demo. Como todos los
specs comparten la misma API y base, el runner usa un solo worker.

## Variables de entorno

Las variables de .NET siguen el formato `Sección__Clave`. Las del front deben
empezar con `VITE_`.

| Variable | Default | Descripción |
|---|---|---|
| `DATA__ROOT` | `../../data` | Raíz única de datos, relativa al proyecto de la API. De ella se derivan SQLite y uploads. |
| `APP__TOKEN` | vacío | Token estático de la instancia personal. Obligatorio en `Production` (salvo `APP__ALLOWANONYMOUS=true`). |
| `APP__ALLOWANONYMOUS` | `false` | Escape hatch explícito para una instancia personal sin token en `Production`. |
| `DEMO__ENABLED` | `false` | Modo demo: siembra datos, usa su propia raíz y rechaza todas las escrituras con `403`. |
| `DEMO__DATAROOT` | `<DATA__ROOT>/demo` | Raíz de datos de la demo. No puede apuntar a la base ni a los uploads personales. |
| `AI__PROVIDER` | `fake` | `fake` o `gemini`. |
| `AI__APIKEY` | vacío | Obligatoria cuando `AI__PROVIDER=gemini`. |
| `AI__MODEL` | `gemini-2.0-flash` | Modelo usado por el analizador real. |
| `AI__DAILYANALYSISLIMIT` | `50` | Análisis de IA permitidos por día (calendario UTC). |
| `AI__DAILYSUGGESTIONLIMIT` | `20` | Sugerencias de outfits permitidas por día (calendario UTC). Comparte el contador atómico en SQLite. |
| `RATELIMITING__ENABLED` | `true` | Rate limit por IP para `/api/garments` y `/api/outfits/suggest`. En `Testing` queda desactivado salvo `RATELIMITING__ENABLED=true` explícito. |
| `RATELIMITING__PERMITLIMIT` | `60` | Solicitudes por minuto y por IP en esas rutas; el exceso responde `429` (`ProblemDetails` en español) con `Retry-After`. |
| `CONNECTIONSTRINGS__DEFAULT` | derivada de `DATA__ROOT` | Override de SQLite para CI/tests. Si se define, se usa tal cual. |
| `STORAGE__ROOT` | `<DATA__ROOT>/uploads` | Override de la carpeta de imágenes para CI/tests. |
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
- Usa una raíz propia: `data/demo` por defecto (base `data/demo/fashion.db` y
  uploads `data/demo/uploads`). Nunca comparte `data/fashion.db` ni
  `data/uploads` con la instancia personal.
- Al arrancar verifica que todas las prendas de la base pertenezcan al manifiesto
  de demo; si encuentra una prenda personal, se niega a arrancar con un error en
  español.
- Solo siembra si la tabla de prendas está vacía: reiniciar no duplica nada.
- En demo, la UI y el backend son de solo lectura (las escrituras devuelven
  `403`) y no se llama a la IA.
- Para volver al estado inicial: detené el backend y borrá **solo**
  `data/demo/`. No borres `data/` completo: ahí vive tu base personal.

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
| GET | `/api/outfits` | Outfits guardados con sus prendas, orden `favorito` y luego `creado` descendente. Incluye prendas archivadas para conservar el collage. |
| POST | `/api/outfits` | Crea un outfit manual validado. Acepta `source` (`manual` por defecto o `ai`) y `rationale` opcional. |
| PATCH | `/api/outfits/{id}` | Edita nombre, ocasión, favorito o reemplaza las prendas (revalida las reglas). |
| DELETE | `/api/outfits/{id}` | Elimina el outfit y sus ítems; nunca borra prendas. |
| POST | `/api/outfits/suggest` | Sugerencias por `occasion` y `weather` opcional (`frio`, `templado`, `calor`; si falta o está vacío se asume `templado`). Devuelve borradores ya normalizados y sin persistir. Consume cuota `suggestion`. |
| GET | `/media/{hash}.webp` | Imagen normalizada, con caché inmutable. |
| GET | `/media/{hash}-thumb.webp` | Miniatura de 400 px. |

Los errores usan `ProblemDetails` (RFC 7807) con mensajes en español. Cuando la
IA responde `{"error":"no_garment"}`, los endpoints de análisis devuelven `422`
("No se detectó una prenda") en vez de guardar una prenda vacía. Los errores de
composición de outfits devuelven `400`, una prenda inexistente o archivada
devuelve `409` y la cuota agotada devuelve `429` con `Retry-After` (expuesto por
CORS para que la UI lo muestre en minutos). Los endpoints de digest todavía no
están implementados (Fase 3).

### Flujo de sugerencias

1. `GET /api/garments` (paginado por cursor) llena el guardarropa del panel. Si
   no existe al menos un conjunto válido —`top` + `bottom` + `shoes`, o `dress`
   + `shoes`, con formalidad dentro de 2 puntos— el formulario queda deshabilitado
   y explica el motivo.
2. `POST /api/outfits/suggest` devuelve hasta tres borradores con `name`,
   `garmentIds`, `occasion` y `rationale`. Nada se persiste.
3. Cada tarjeta muestra el collage con las fotos reales y la razón de la
   propuesta. "Guardar" llama a `POST /api/outfits` con `source=ai` y el
   `rationale`; el listado se invalida y la tarjeta aparece con la insignia IA.
4. Una respuesta vacía muestra "sin resultados"; los errores se pueden
   reintentar y el `429` informa cuántos minutos faltan para el reinicio UTC.
5. El servidor valida toda propuesta contra prendas activas antes de devolverla:
   ids inventados o composiciones inválidas se descartan y, si no queda ninguna,
   se usa el generador por reglas.

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
  Si la prenda estaba archivada, volver a subir/confirmar la foto restaura la
  misma fila con los valores confirmados en lugar de crear un duplicado.
- **Filtros completos.** La UI expone categoría, color (texto libre con
  sugerencias de los colores cargados) y temporada. El color se debouncea 300 ms
  para no disparar un request por tecla.
- **Paginación acumulativa.** El guardarropa usa `useInfiniteQuery` y el botón
  "Cargar más": nunca se pierden las páginas ya cargadas al cambiar de filtro.
- **Vocabulario cerrado también en la UI.** Las etiquetas son chips de una lista
  de 18 valores (máximo 4); confirm y PATCH validan longitudes, temporadas,
  etiquetas y `colorHex` (`#RRGGBB`) y responden `400` en español si algo no
  cumple. La IA sigue reparando respuestas parciales en vez de fallar.
- **Privacidad de la foto.** El diálogo avisa en español que la imagen se envía
  al proveedor de IA configurado para analizarla. Los tests usan `fake` y nunca
  llaman a un proveedor real.
- **Color con muestra.** El formulario acepta `colorName` y `colorHex`; el hex se
  guarda en confirm/PATCH y la tarjeta lo usa para el círculo de color.
- **Demo de solo lectura.** `GET /api/config` expone `{ demo }`; la UI oculta las
  acciones de escritura y explica en español por qué no se puede subir.
- **Sin librería de UI.** Tailwind 4 y componentes propios; rutas con
  `React.lazy` para no cargar el guardarropa en la home.
- **IA fake en tests.** Ni los tests de backend ni el E2E llaman a un proveedor
  real.

## Decisiones de la Fase 2

- **Outfits como collage de prendas reales.** `Outfit` guarda ítems con `Layer`
  y `SortOrder`; el listado ordena por `IsFavorite` y `CreatedAt` y conserva las
  fotos incluso si una prenda se archiva después.
- **Sugerencias sin persistir.** `POST /api/outfits/suggest` devuelve borradores;
  solo "Guardar" crea el outfit. `source` se persiste en minúsculas (`ai` o
  `manual`), por eso el DTO expone el mismo vocabulario.
- **Validación en dos capas.** La UI deshabilita Guardar con reglas locales
  (composición + formalidad); el servidor revalida siempre y descarta propuestas
  de IA con ids inventados o composiciones inválidas antes de responder.
- **Cuota y caché de sugerencias.** La caché en memoria dura 10 minutos por
  `occasion|weather|count|maxUpdatedAt` y no consume cuota; un miss consume
  `Ai:DailySuggestionLimit` (default 20) y responde `429` con `Retry-After`.
- **Fallback determinista.** Si el proveedor falla o devuelve propuestas
  inválidas, se reintenta una vez y luego se usa el generador por reglas. Los
  tests usan `Ai:Provider=fake` para que el E2E sea determinista.
- **Límites de guardado en las sugerencias.** El servicio recorta los textos
  (`name` 120, `occasion` 60, `rationale` 600), descarta las propuestas con más
  de 5 prendas (nunca las trunca, para no ocultar ids repetidos) y limita la
  respuesta a 3 borradores. Un borrador que deja de ser válido tras la
  normalización se descarta, así que todo lo que se muestra o se cachea se
  puede guardar con `POST /api/outfits`.
- **Rate limit por IP.** `/api/garments` y `/api/outfits/suggest` comparten una
  ventana fija de 60 solicitudes por minuto (configurable); el exceso devuelve
  `429` con `ProblemDetails` en español y `Retry-After`.

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
