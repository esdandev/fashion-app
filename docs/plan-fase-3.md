# Fashion App — Plan de implementación: Fase 3 (Hoy)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mostrar cada día un resumen de moda generado desde feeds RSS, cacheado en SQLite, con enlaces a las fuentes y relación con las prendas del guardarropa por `StyleTags`.

**Architecture:** Un `BackgroundService` diario descarga feeds, deduplica y resume con IA, y persiste un `Digest` por fecha. `GET /api/digest/today` sirve el último digest con ETag; las prendas relacionadas solo se incluyen si la instancia no tiene token o el request lo trae. El frontend reemplaza el placeholder de “Hoy” por la portada del producto.

**Tech Stack:** .NET 10 Minimal APIs, EF Core/SQLite, `System.ServiceModel.Syndication`, Gemini + fake, xUnit; React 19 + TypeScript + Vite + Tailwind 4, TanStack Query, Vitest, Playwright.

**Spec:** `00 - Projects/Fashion App/Fashion App.md`

## Índice del plan

- [[#Global Constraints]]
- [[#Review Focus]]
- [[#File Structure]]
- [[#Task 1: Modelo Digest, lectura pública y matching]]
- [[#Task 2: Feeds, resumen IA y validación]]
- [[#Task 3: Servicio de generación y persistencia]]
- [[#Task 4: BackgroundService y endpoint de refresh]]
- [[#Task 5: Portada Hoy en el frontend]]
- [[#Task 6: E2E, documentación y cierre]]

## Global Constraints

- .NET 10, EF Core/SQLite; migraciones con `dotnet ef migrations add`.
- React 19 + TypeScript + Vite + Tailwind 4; UI y errores en español.
- Timestamps `DateTimeOffset` UTC (conversor UTC-ticks existente); la fecha del digest es `DateOnly`.
- PKs `Guid`; lecturas con `AsNoTracking` y proyección; sin N+1.
- `/api/digest/today` es **público** (sin token) y debe seguir siendo útil en la demo.
- Las prendas relacionadas son datos personales: solo se incluyen cuando `App:Token` está vacío o el request trae un `X-App-Token` válido. Sin token, `relatedGarments` es `[]`.
- `/api/digest/refresh` exige `X-Admin-Secret` = `Digest:AdminSecret`, con cooldown de 1 hora y `429` + `Retry-After`.
- La ingesta no copia el cuerpo de los artículos: título, resumen propio y link a la fuente.
- Los tests nunca llaman a feeds ni a IA reales: `Digest:Provider=fake` y `Ai:Provider=fake`.
- Rate limiting existente no se aplica a `/api/digest/today` (lectura pública cacheada); sí a `refresh` como endpoint sensible.
- Presupuesto: `GET /api/digest/today` responde en < 100 ms con caché y soporta `304`.
- Las imágenes de noticias se muestran con `loading="lazy"` y dimensiones estables; si no hay imagen, se usa un bloque de color.

## Review Focus

1. **Feed caído o XML inválido** → se omiten esas entradas, el resto del digest se genera; si no queda nada, se conserva el digest anterior, nunca se guarda uno vacío.
2. **URL inventada por la IA** → se descarta el ítem; solo se aceptan URLs presentes en los feeds originales.
3. **Fuga de privacidad** → con token configurado y sin header, `/api/digest/today` responde igual pero `relatedGarments` vacío; con header válido, incluye hasta 3 prendas.
4. **Dos ejecuciones el mismo día** → el digest de la fecha se reemplaza de forma transaccional; no hay filas duplicadas ni ítems huérfanos.
5. **Refresh sin secreto o durante el cooldown** → `401`/`403` claro y `429` con `Retry-After`; nunca regenera la IA de gratis.

---

## File Structure

```
backend/FashionApp.Api/
├─ Domain/Digest.cs
├─ Domain/DigestItem.cs
├─ Features/Digest/
│  ├─ DigestContracts.cs
│  ├─ DigestEndpoints.cs
│  ├─ DigestRefreshEndpoint.cs
│  ├─ DigestGenerator.cs
│  ├─ DigestMatching.cs
│  └─ DigestValidator.cs
├─ Infrastructure/Feeds/
│  ├─ IFeedFetcher.cs
│  ├─ HttpFeedFetcher.cs
│  └─ FakeFeedFetcher.cs
├─ Infrastructure/Ai/
│  ├─ IDigestSummarizer.cs
│  ├─ FakeDigestSummarizer.cs
│  ├─ GeminiDigestSummarizer.cs
│  └─ Prompts/digest-summary.md
└─ Infrastructure/Digest/DigestBackgroundService.cs

frontend/src/features/digest/
├─ TodayPage.tsx
├─ DigestCard.tsx
└─ digestFormat.ts

frontend/tests/digest.spec.ts
```

---

### Task 1: Modelo Digest, lectura pública y matching

**Files:**
- Create: `backend/FashionApp.Api/Domain/Digest.cs`, `backend/FashionApp.Api/Domain/DigestItem.cs`, `backend/FashionApp.Api/Features/Digest/DigestContracts.cs`, `backend/FashionApp.Api/Features/Digest/DigestMatching.cs`, `backend/FashionApp.Api/Features/Digest/DigestEndpoints.cs`, `backend/FashionApp.Tests/DigestReadTests.cs`
- Modify: `backend/FashionApp.Api/Infrastructure/AppDbContext.cs`, `backend/FashionApp.Api/Program.cs`, `backend/FashionApp.Api/Features/Security/AccessFilters.cs`
- Migration: `dotnet ef migrations add AddDigests --project backend/FashionApp.Api`

**Interfaces:**
- Consumes: `Garment`, `GarmentQuery.ToDto`, `AppSecurityOptions`, `AccessFilters` helpers.
- Produces:
  - `sealed class Digest { Guid Id; DateOnly Date; string Summary; DateTimeOffset GeneratedAt; List<DigestItem> Items; }`
  - `sealed class DigestItem { Guid Id; Guid DigestId; string Title; string Url; string Source; string Summary; string StyleTagsJson; string? ImageUrl; DateTimeOffset? PublishedAt; int SortOrder; }`
  - `record DigestItemDto(Guid Id, string Title, string Url, string Source, string Summary, string[] StyleTags, string? ImageUrl, DateTimeOffset? PublishedAt, IReadOnlyList<GarmentDto> RelatedGarments);`
  - `record DigestDto(DateOnly Date, string Summary, DateTimeOffset GeneratedAt, IReadOnlyList<DigestItemDto> Items);`
  - `GET /api/digest/today` → `200 DigestDto` del digest más reciente (hoy o el último), `404` si no hay ninguno; `ETag` y `304` con `If-None-Match`.
  - `static class DigestMatching { static IReadOnlyList<GarmentDto> Match(IReadOnlyList<Garment> active, IReadOnlyList<string> tags, int max = 3); }`

- [ ] **Step 1: Escribir los tests que fallan**

```csharp
[Fact]
public async Task Today_returns_digest_with_items_and_related_garments()
{
    await SeedDigest(factory, tags: ["minimalista"]);
    await SeedGarmentWithTags(factory, "Camisa lino", ["minimalista"]);

    var response = await factory.CreateClient().GetAsync("/api/digest/today");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var digest = await response.Content.ReadFromJsonAsync<DigestDto>();
    var item = Assert.Single(digest!.Items);
    Assert.Equal("https://example.com/nota", item.Url);
    Assert.Equal("Camisa lino", Assert.Single(item.RelatedGarments).Name);
}

[Fact]
public async Task Today_omits_related_garments_without_token_on_a_private_instance()
{
    using var privateFactory = new TestAppFactory { Token = "secreto" };
    await SeedDigest(privateFactory, tags: ["minimalista"]);
    await SeedGarmentWithTags(privateFactory, "Camisa lino", ["minimalista"]);

    var anonymous = await privateFactory.CreateClient().GetFromJsonAsync<DigestDto>("/api/digest/today");
    Assert.Empty(anonymous!.Items[0].RelatedGarments);

    var client = privateFactory.CreateClient();
    client.DefaultRequestHeaders.Add("X-App-Token", "secreto");
    var authorized = await client.GetFromJsonAsync<DigestDto>("/api/digest/today");
    Assert.Single(authorized!.Items[0].RelatedGarments);
}

[Fact]
public async Task Today_supports_etag_and_304()
{
    await SeedDigest(factory, tags: ["casual"]);
    var client = factory.CreateClient();
    var first = await client.GetAsync("/api/digest/today");
    var etag = first.Headers.ETag!.Tag;
    var request = new HttpRequestMessage(HttpMethod.Get, "/api/digest/today");
    request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));
    var second = await client.SendAsync(request);
    Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~DigestReadTests"`
Expected: FAIL de compilación.

- [ ] **Step 3: Implementar entidades, configuración y migración**

`Digest.Date` único; `DigestItem` con índice único `(DigestId, Url)` (la misma nota puede aparecer en días consecutivos, por eso no es único global); índice `(DigestId, SortOrder)`; FK cascade. `StyleTagsJson` máximo 300, `Summary` 600, `Title` 200, `Source` 80, `ImageUrl` 500. Crear la migración `AddDigests`.

- [ ] **Step 4: Implementar lectura, ETag y matching**

`GET /api/digest/today`:
- busca el digest con la fecha más reciente (`OrderByDescending(Date).FirstOrDefault`);
- proyecta ítems ordenados por `SortOrder`;
- calcula `relatedGarments` solo si `AppSecurityOptions.Token` está vacío o el header coincide (exponer `AccessFilters.HasValidToken(HttpContext)` público y reutilizar la comparación de tiempo fijo);
- calcula `ETag = "\"{digest.Id:N}-{digest.GeneratedAt.UtcTicks}\""`; si `If-None-Match` coincide → `304`;
- `Cache-Control: private, max-age=300` y `Vary: X-App-Token`: la respuesta cambia según el token, así que nunca debe cachearse como pública.

`DigestMatching.Match` parsea `StyleTagsJson` de cada prenda, intersecta con los tags del ítem, ordena por cantidad de coincidencias y devuelve hasta 3.

- [ ] **Step 5: Correr los tests y la suite**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~DigestReadTests"`
Expected: PASS. Luego la suite completa.

- [ ] **Step 6: Commit**

```bash
git add backend
git commit -m "feat: add digest model, public read endpoint and garment matching"
```

---

### Task 2: Feeds, resumen IA y validación

**Files:**
- Create: `backend/FashionApp.Api/Infrastructure/Feeds/IFeedFetcher.cs`, `backend/FashionApp.Api/Infrastructure/Feeds/HttpFeedFetcher.cs`, `backend/FashionApp.Api/Infrastructure/Feeds/FakeFeedFetcher.cs`, `backend/FashionApp.Api/Infrastructure/Ai/IDigestSummarizer.cs`, `backend/FashionApp.Api/Infrastructure/Ai/FakeDigestSummarizer.cs`, `backend/FashionApp.Api/Infrastructure/Ai/GeminiDigestSummarizer.cs`, `backend/FashionApp.Api/Infrastructure/Ai/Prompts/digest-summary.md`, `backend/FashionApp.Api/Features/Digest/DigestValidator.cs`, `backend/FashionApp.Tests/DigestIngestTests.cs`
- Modify: `backend/FashionApp.Api/FashionApp.Api.csproj`, `backend/FashionApp.Api/Features/Digest/DigestContracts.cs`, `backend/FashionApp.Api/Program.cs`, `backend/FashionApp.Api/appsettings.json`

**Interfaces:**
- Consumes: `AiOptions`, `StubHttpHandler`, `GarmentVocabulary`.
- Produces:
  - `sealed record FeedSource(string Name, string Url);`
  - `sealed record FeedEntry(string Title, string Url, string Source, DateTimeOffset? PublishedAt, string? Summary, string? ImageUrl);`
  - `interface IFeedFetcher { Task<IReadOnlyList<FeedEntry>> FetchAsync(FeedSource source, CancellationToken ct); }`
  - `sealed record DigestDraft(string Title, string Url, string Source, string Summary, string[] StyleTags, string? ImageUrl, DateTimeOffset? PublishedAt);`
  - `sealed record DigestSummary(string? Summary, IReadOnlyList<DigestDraft> Items);`
  - `interface IDigestSummarizer { Task<DigestSummary> SummarizeAsync(IReadOnlyList<FeedEntry> entries, CancellationToken ct); }`
  - `static class DigestValidator { static DigestSummary Clean(DigestSummary summary, IReadOnlyList<FeedEntry> sources, int maxItems); }`

- [ ] **Step 1: Escribir los tests que fallan**

```csharp
[Fact]
public void Validator_drops_urls_not_present_in_the_feeds_and_unknown_tags()
{
    var sources = new[] { Entry("Nota real", "https://example.com/real") };
    var summary = new DigestSummary("Resumen", [
        new DigestDraft("Buena", "https://example.com/real", "Vogue", "Resumen", ["minimalista", "inventado"], null, null),
        new DigestDraft("Inventada", "https://example.com/falsa", "Vogue", "Resumen", ["casual"], null, null),
    ]);

    var clean = DigestValidator.Clean(summary, sources, maxItems: 10);
    var item = Assert.Single(clean.Items);
    Assert.Equal("https://example.com/real", item.Url);
    Assert.Equal(["minimalista"], item.StyleTags);
}

[Fact]
public void Validator_caps_items_and_trims_text()
{
    var sources = Enumerable.Range(0, 15).Select(i => Entry($"N{i}", $"https://example.com/{i}")).ToArray();
    var summary = new DigestSummary(null, sources.Select(e =>
        new DigestDraft(new string('t', 300), e.Url, "Vogue", new string('s', 900), ["casual"], null, null)).ToList());

    var clean = DigestValidator.Clean(summary, sources, maxItems: 10);
    Assert.Equal(10, clean.Items.Count);
    Assert.Equal(200, clean.Items[0].Title.Length);
    Assert.Equal(600, clean.Items[0].Summary.Length);
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~DigestIngestTests"`
Expected: FAIL de compilación.

- [ ] **Step 3: Implementar feeds**

`dotnet add backend/FashionApp.Api package System.ServiceModel.Syndication`.

`HttpFeedFetcher`:
- `HttpClient` con timeout 10 s y `User-Agent` propio;
- parsea RSS 2.0 y Atom con `SyndicationFeed.LoadAsync`;
- solo toma los primeros 15 ítems por feed;
- mapea link, título, fecha, resumen corto (máx. 400 chars) e imagen (`media:content`/enclosure si existe);
- cualquier excepción devuelve lista vacía (el generador decide).

`FakeFeedFetcher` devuelve 8 entradas deterministas de moda con URLs `https://example.com/nota-N`.

- [ ] **Step 4: Implementar resumen IA**

`FakeDigestSummarizer` devuelve un resumen fijo y un draft por entrada, con tags `["casual"]` (y `"minimalista"` en la primera) y título recortado.

`GeminiDigestSummarizer`:
- prompt `digest-summary.md` con hasta 20 entradas `{title, url, source, publishedAt, summary}`;
- pide 6–10 ítems en español con `title`, `url` (de la lista), `summary` (2–3 líneas) y `styleTags` (1–3 del vocabulario);
- `responseMimeType=application/json`, temperatura 0.3, timeout 20 s;
- parsea `{ "summary": "...", "items": [...] }`; cualquier fallo devuelve `new DigestSummary(null, [])`.

`DigestValidator.Clean`:
- descarta URLs que no estén en `sources`;
- descarta tags fuera del vocabulario y deja 1–3;
- recorta título 200, resumen 600, source 80;
- deduplica por URL;
- ordena por `PublishedAt` descendente y corta a `maxItems`.

- [ ] **Step 5: Registrar por configuración**

`Digest:Provider` (`rss` | `fake`, default `fake` en Development) para `IFeedFetcher`; `Ai:Provider` para `IDigestSummarizer` (Gemini si `gemini`, fake si no). Si `Ai:Provider=gemini` falta la clave, ya falla el arranque existente.

- [ ] **Step 6: Correr los tests y la suite**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~DigestIngestTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend
git commit -m "feat: add RSS feeds, digest summarizer and validation"
```

---

### Task 3: Servicio de generación y persistencia

**Files:**
- Create: `backend/FashionApp.Api/Features/Digest/DigestGenerator.cs`, `backend/FashionApp.Tests/DigestGeneratorTests.cs`
- Modify: `backend/FashionApp.Api/Program.cs`

**Interfaces:**
- Consumes: `IFeedFetcher`, `IDigestSummarizer`, `DigestValidator`, `AppDbContext`, `IConfiguration`, `ILogger<DigestGenerator>`.
- Produces:
  - `interface IDigestGenerator { Task<Digest?> GenerateAsync(CancellationToken ct); }`
  - `DigestGenerator` con dependencias inyectadas.

- [ ] **Step 1: Escribir los tests que fallan**

```csharp
[Fact]
public async Task Generation_dedupes_and_persists_one_digest_per_date()
{
    var generator = Build(feeds: [
        Entry("Misma nota", "https://example.com/a"),
        Entry("Misma nota", "https://example.com/a"),
        Entry("Otra", "https://example.com/b"),
    ], drafts: 2);

    var digest = await generator.GenerateAsync(default);
    Assert.NotNull(digest);
    Assert.Equal(2, digest!.Items.Count);

    var again = await generator.GenerateAsync(default);
    await using var db = NewDb();
    Assert.Single(await db.Digests.ToListAsync());
    Assert.Equal(2, await db.DigestItems.CountAsync());
}

[Fact]
public async Task Empty_result_keeps_the_previous_digest()
{
    await SeedExistingDigest();
    var generator = Build(feeds: [], drafts: 0);
    Assert.Null(await generator.GenerateAsync(default));
    await using var db = NewDb();
    Assert.Single(await db.Digests.ToListAsync());
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~DigestGeneratorTests"`
Expected: FAIL de compilación.

- [ ] **Step 3: Implementar el generador**

1. Lee `Digest:Feeds` (lista de `{ Name, Url }`); si está vacía, usa `FakeFeedFetcher` en Development o devuelve `null` en Production con un warning.
2. Fetch en paralelo de todos los feeds con timeout; errores se loggean y se omiten.
3. Filtra por `PublishedAt >= now - Digest:MaxAgeHours` (48) cuando hay fecha; deduplica por URL y por título normalizado.
4. Toma hasta 20 entradas y llama al summarizer.
5. `DigestValidator.Clean` con `Digest:MaxItems` (10).
6. Si no quedan ítems, devuelve `null` y no toca la base.
7. Upsert transaccional del `Digest` de `DateOnly.FromDateTime(UtcNow)`: si existe, borra sus ítems y reinserta; `Summary` y `GeneratedAt` se actualizan.
8. Devuelve el digest persistido.

- [ ] **Step 4: Registrar `IDigestGenerator`**

Scoped, con `TimeProvider` inyectado para poder testear la fecha.

- [ ] **Step 5: Correr los tests y la suite**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~DigestGeneratorTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend
git commit -m "feat: generate and persist the daily digest"
```

---

### Task 4: BackgroundService y endpoint de refresh

**Files:**
- Create: `backend/FashionApp.Api/Infrastructure/Digest/DigestBackgroundService.cs`, `backend/FashionApp.Api/Features/Digest/DigestRefreshEndpoint.cs`, `backend/FashionApp.Api/Features/Digest/DigestRefreshGuard.cs`, `backend/FashionApp.Tests/DigestRefreshTests.cs`
- Modify: `backend/FashionApp.Api/Program.cs`, `backend/FashionApp.Api/appsettings.json`

**Interfaces:**
- Consumes: `IDigestGenerator`, `TimeProvider`, `IConfiguration`, `AppSecurityOptions`.
- Produces:
  - `sealed class DigestRefreshGuard(TimeProvider clock) { bool TryBegin(); TimeSpan? Remaining(); }` con cooldown de 1 hora.
  - `POST /api/digest/refresh` con header `X-Admin-Secret` → `200 DigestDto`; `401` sin secreto, `403` secreto incorrecto o no configurado, `429` con `Retry-After` durante el cooldown, `500`/`502` si la generación no produce digest (sin tocar el anterior).
  - `DigestBackgroundService : BackgroundService` que ejecuta al arrancar (si no hay digest de hoy y `Digest:GenerateOnStartup=true`) y luego cada día a `Digest:RunAt` (default `07:00`, hora local del servidor).

- [ ] **Step 1: Escribir los tests que fallan**

```csharp
[Fact]
public async Task Refresh_requires_the_admin_secret()
{
    using var factory = new TestAppFactory { AdminSecret = "s3creto" };
    var client = factory.CreateClient();
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/digest/refresh", null)).StatusCode);

    client.DefaultRequestHeaders.Add("X-Admin-Secret", "otro");
    Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/digest/refresh", null)).StatusCode);

    client.DefaultRequestHeaders.Remove("X-Admin-Secret");
    client.DefaultRequestHeaders.Add("X-Admin-Secret", "s3creto");
    var ok = await client.PostAsync("/api/digest/refresh", null);
    Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
}

[Fact]
public async Task Refresh_is_rate_limited_by_a_one_hour_cooldown()
{
    var guard = new DigestRefreshGuard(new ManualTimeProvider(DateTimeOffset.Parse("2026-11-08T10:00:00Z")));
    Assert.True(guard.TryBegin());
    Assert.False(guard.TryBegin());
    Assert.True(guard.Remaining() > TimeSpan.Zero);
    guard.Clock.Advance(TimeSpan.FromHours(1));
    Assert.True(guard.TryBegin());
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~DigestRefreshTests"`
Expected: FAIL.

- [ ] **Step 3: Implementar el guard y el endpoint**

`DigestRefreshGuard` con `lock` y `DateTimeOffset? _last`; `TryBegin` compara con `clock.GetUtcNow()`; `Remaining` devuelve el tiempo restante o `null`. Registrar singleton.

El endpoint:
- exige secreto con comparación de tiempo fijo;
- aplica rate limit por IP existente (`ClientRateLimiting`) además del cooldown;
- llama a `IDigestGenerator.GenerateAsync`; si devuelve `null`, `502` ProblemDetails ("No se pudo generar el digest; se conserva el anterior.");
- devuelve el `DigestDto` con matching solo si hay token (misma regla que `today`).

- [ ] **Step 4: Implementar el BackgroundService**

Al arrancar:
- si `Digest:GenerateOnStartup` y no hay digest de hoy, genera una vez;
- calcula el próximo `RunAt` local, espera con `PeriodicTimer`/`Task.Delay` y repite;
- usa `IServiceScopeFactory` para crear el scope;
- captura y loggea excepciones sin tumbar el host.

- [ ] **Step 5: Configurar y correr los tests**

`appsettings.json`: `Digest` con `Provider`, `RunAt`, `GenerateOnStartup`, `MaxItems`, `MaxAgeHours`, `AdminSecret`, `Feeds`.

Agregar `AdminSecret` a `TestAppFactory` (config `Digest:AdminSecret`) y `ManualTimeProvider` en `TestData`.

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~DigestRefreshTests"`
Expected: PASS. Luego la suite completa.

- [ ] **Step 6: Commit**

```bash
git add backend
git commit -m "feat: add daily digest job and admin refresh endpoint"
```

---

### Task 5: Portada Hoy en el frontend

**Files:**
- Create: `frontend/src/features/digest/DigestCard.tsx`, `frontend/src/features/digest/digestFormat.ts`, `frontend/src/features/digest/digestFormat.test.ts`
- Modify: `frontend/src/features/digest/TodayPage.tsx`, `frontend/src/lib/types.ts`, `frontend/src/lib/api.ts`

**Interfaces:**
- Consumes: `GET /api/digest/today`.
- Produces:
  - `DigestDto`, `DigestItemDto` en `types.ts`.
  - `api.getDigest(signal)`.
  - `formatDigestDate(date: string): string` → "domingo 8 de noviembre" en español.
  - `TodayPage` con hero, resumen, tarjetas, tags, prendas relacionadas, link a la fuente, skeletons, vacío y error.

- [ ] **Step 1: Escribir el test que falla**

```ts
import { describe, expect, it } from 'vitest'
import { formatDigestDate } from './digestFormat'

describe('formatDigestDate', () => {
  it('formats an ISO date in Spanish', () => {
    expect(formatDigestDate('2026-11-08')).toBe('domingo 8 de noviembre')
  })
})
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `cd frontend && pnpm vitest run src/features/digest/digestFormat.test.ts`
Expected: FAIL.

- [ ] **Step 3: Implementar formato, tipos y API**

`formatDigestDate` usa `Intl.DateTimeFormat('es-AR', { weekday: 'long', day: 'numeric', month: 'long', timeZone: 'UTC' })` sobre `new Date(date + 'T00:00:00Z')`.

`api.getDigest(signal?)` con `authHeaders()` (opcional; el endpoint es público) y `parseResponse`.

- [ ] **Step 4: Implementar la portada**

- Hero: fecha larga, `summary` del digest y contador de novedades.
- Cada tarjeta: título (link externo con `target="_blank" rel="noopener noreferrer"`), fuente, resumen, chips de tags, hasta 3 miniaturas de prendas relacionadas con `loading="lazy"` y `width/height`.
- Estados: skeleton con altura estable, vacío ("Todavía no hay resumen de hoy"), error con reintento.
- Si `relatedGarments` viene vacío, no se muestra la sección (no un placeholder).
- Responsive: una columna en móvil, dos en desktop.

- [ ] **Step 5: Correr tests y build**

Run: `cd frontend && pnpm vitest run && pnpm lint && pnpm build`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend
git commit -m "feat: add today digest page with related garments"
```

---

### Task 6: E2E, documentación y cierre

**Files:**
- Create: `frontend/tests/digest.spec.ts`
- Modify: `frontend/tests/test-backend.mjs`, `README.md`, `docs/Fashion App.md`

**Interfaces:**
- Consumes: API `POST /api/digest/refresh`, `GET /api/digest/today`, fake feed/summarizer.
- Produces: E2E del digest; README actualizado; bitácora de la fase.

- [ ] **Step 1: Configurar el backend de test**

En `frontend/tests/test-backend.mjs`, agregar:
- `DIGEST__PROVIDER=fake`, `DIGEST__GENERATEONSTARTUP=false`, `DIGEST__ADMINSECRET=test-secret`, `DIGEST__FEEDS=[]`.

- [ ] **Step 2: Escribir el E2E que falla**

```ts
test('muestra el resumen del día y lo relaciona con una prenda', async ({ page, request }) => {
  await seedGarment(request, 'camisa-lino.webp', 'Camisa de lino', ['minimalista'])
  const refresh = await request.post('http://localhost:5210/api/digest/refresh', {
    headers: { 'X-Admin-Secret': 'test-secret' },
  })
  expect(refresh.ok()).toBeTruthy()

  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Hoy' })).toBeVisible()
  await expect(page.getByText('Resumen de prueba')).toBeVisible()
  await expect(page.getByRole('link', { name: /nota/i })).toHaveAttribute('target', '_blank')
  await expect(page.getByText('Camisa de lino')).toBeVisible()
})
```

- [ ] **Step 3: Correr el E2E y verificar que falla**

Run: `cd frontend && pnpm exec playwright test tests/digest.spec.ts`
Expected: FAIL porque el backend no genera digest.

- [ ] **Step 4: Implementar y correr todo**

Run: `pnpm exec playwright test`, `pnpm vitest run`, `pnpm build`, `dotnet test backend/FashionApp.sln`.
Expected: PASS.

- [ ] **Step 5: Documentar**

README: endpoints de digest, variables `DIGEST__*`, cómo forzar un refresh, nota legal (título + resumen propio + link), y el flujo de la portada.
`docs/Fashion App.md`: bitácora con la fase y contadores finales; Roadmap marcando la Fase 3.

- [ ] **Step 6: Commit**

```bash
git add frontend README.md docs
git commit -m "feat: complete daily digest with E2E coverage"
```

---

## Siguiente plan

La Fase 4 (portfolio) cubre deploy de la demo, capturas, métricas y README final. Es el último tramo.
