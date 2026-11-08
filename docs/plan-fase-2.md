# Fashion App — Plan de implementación: Fase 2 (Outfits)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sugerir outfits con IA a partir del guardarropa real, validarlos, permitir armarlos y editarlos a mano, y guardarlos como collage de prendas.

**Architecture:** El backend agrega `OutfitItem`, reglas de composición compartidas, un sugeridor de IA detrás de `IOutfitSuggester` (Gemini + fake) con fallback por reglas, cuota por tipo y caché en memoria. El frontend suma la vista de outfits con listado, constructor manual y panel de sugerencias.

**Tech Stack:** .NET 10 Minimal APIs, EF Core/SQLite, Magick.NET, xUnit; React 19 + TypeScript + Vite + Tailwind 4, TanStack Query, React Router, Vitest, Playwright.

**Spec:** `00 - Projects/Fashion App.md`

## Índice del plan

- [[#Global Constraints]]
- [[#Review Focus]]
- [[#File Structure]]
- [[#Task 1: Modelo OutfitItem y listado de outfits]]
- [[#Task 2: Reglas de composición y CRUD manual]]
- [[#Task 3: Sugeridor por reglas y validación de propuestas]]
- [[#Task 4: Sugeridor IA, cuota y endpoint]]
- [[#Task 5: Frontend: tipos, API y listado en collage]]
- [[#Task 6: Constructor manual y edición]]
- [[#Task 7: Panel de sugerencias, E2E y cierre]]

## Global Constraints

- .NET 10, EF Core 10 + SQLite; migraciones generadas con `dotnet ef migrations add`.
- React 19 + TypeScript + Vite + Tailwind 4; UI y errores en español.
- Timestamps `DateTimeOffset` en UTC (el conversor UTC-ticks ya existe).
- PKs `Guid` generadas en el servidor.
- Lecturas con `AsNoTracking()` y proyección; prohibido N+1.
- Tokens: la instancia personal exige `X-App-Token` cuando `App:Token` no está vacío. La demo bloquea POST/PATCH/DELETE con `403` **antes** del token.
- Los tests nunca llaman a un proveedor de IA real: `Ai:Provider=fake`.
- Toda propuesta de IA se valida contra prendas activas; ids inventados se descartan.
- Reglas de outfit válido (del spec):
  - `top` + `bottom`, o un `dress`.
  - `shoes` obligatorio.
  - Como máximo un `outerwear`.
  - `bag` y `accessory` opcionales, sin límite.
  - Sin prendas repetidas; sin dos `top`, dos `bottom` ni dos `dress`.
  - La formalidad de las prendas no puede diferir en más de 2 puntos.
  - Solo prendas activas (`IsArchived = false`).
- Presupuesto: la sugerencia no bloquea listados; timeout de 20 s; la UI muestra skeletons.
- El collage usa las fotos reales; nunca se generan imágenes de personas.
- No se agregan endpoints de outfits al modo demo de escritura; la demo solo lista outfits sembrados si existen.

## Review Focus

1. **Propuesta de IA con ids inventados o repetidos** → se descarta el outfit; si no queda ninguno, se usa el fallback por reglas, nunca un 500.
2. **Outfit manual inválido** (sin calzado, dos pantalones, formalidad dispersa) → `400` con ProblemDetails en español y sin persistir.
3. **Prenda archivada** → no puede entrar a un outfit nuevo; si una prenda de un outfit guardado se archiva, el outfit sigue mostrándose con la foto archivada.
4. **Cuota de sugerencias agotada** → `429` con `Retry-After`, sin llamar a la IA.
5. **Cancelar/editar** → editar un outfit no crea duplicados; eliminar borra el outfit y sus ítems, nunca las prendas.

---

## File Structure

```
backend/FashionApp.Api/
├─ Domain/Outfit.cs                    # + Items
├─ Domain/OutfitItem.cs
├─ Features/Outfits/
│  ├─ OutfitContracts.cs
│  ├─ OutfitRules.cs
│  ├─ OutfitEndpoints.cs
│  ├─ OutfitSuggestionContracts.cs
│  ├─ OutfitSuggestionValidator.cs
│  ├─ OutfitSuggestionService.cs
│  └─ OutfitSuggestEndpoint.cs
├─ Infrastructure/Ai/
│  ├─ IOutfitSuggester.cs
│  ├─ FakeOutfitSuggester.cs
│  ├─ GeminiOutfitSuggester.cs
│  └─ Prompts/outfit-suggestions.md
└─ Infrastructure/Quotas/AiQuotaService.cs   # límite por tipo

frontend/src/
├─ features/outfits/
│  ├─ OutfitsPage.tsx
│  ├─ OutfitCard.tsx
│  ├─ OutfitCollage.tsx
│  ├─ OutfitBuilder.tsx
│  ├─ OutfitEditor.tsx
│  ├─ SuggestionPanel.tsx
│  └─ outfitRules.ts
├─ lib/api.ts
└─ lib/types.ts

frontend/tests/outfits.spec.ts
```

---

### Task 1: Modelo OutfitItem y listado de outfits

**Files:**
- Create: `backend/FashionApp.Api/Domain/OutfitItem.cs`, `backend/FashionApp.Api/Features/Outfits/OutfitContracts.cs`, `backend/FashionApp.Api/Features/Outfits/OutfitEndpoints.cs`, `backend/FashionApp.Tests/OutfitListTests.cs`
- Modify: `backend/FashionApp.Api/Domain/Outfit.cs`, `backend/FashionApp.Api/Infrastructure/AppDbContext.cs`, `backend/FashionApp.Api/Program.cs`
- Migration: `dotnet ef migrations add AddOutfitItems --project backend/FashionApp.Api`

**Interfaces:**
- Consumes: `GarmentDto`, `AppDbContext`, `AccessFilters`.
- Produces:
  - `sealed class OutfitItem { Guid OutfitId; Guid GarmentId; int Layer; int SortOrder; Outfit Outfit; Garment Garment; }`
  - `Outfit.Items` (`List<OutfitItem>`).
  - `record OutfitItemDto(GarmentDto Garment, int Layer, int SortOrder);`
  - `record OutfitDto(Guid Id, string Name, string? Occasion, string Source, string? Rationale, bool IsFavorite, DateTimeOffset CreatedAt, IReadOnlyList<OutfitItemDto> Items);`
  - `GET /api/outfits` → `IReadOnlyList<OutfitDto>`, orden `IsFavorite DESC, CreatedAt DESC`, token requerido.

- [ ] **Step 1: Escribir el test que falla**

```csharp
[Fact]
public async Task Lists_saved_outfits_with_their_garments()
{
    var (outfitId, garmentId) = await SeedOutfitWithGarment(factory, "Look de trabajo");
    var response = await factory.CreateClient().GetAsync("/api/outfits");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var outfits = await response.Content.ReadFromJsonAsync<List<OutfitDto>>();
    var outfit = Assert.Single(outfits!);
    Assert.Equal("Look de trabajo", outfit.Name);
    Assert.Equal(garmentId, Assert.Single(outfit.Items).Garment.Id);
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test backend/FashionApp.sln --filter Lists_saved_outfits_with_their_garments`
Expected: FAIL de compilación.

- [ ] **Step 3: Implementar entidad, configuración y migración**

`OutfitItem` con PK compuesta `(OutfitId, GarmentId)`, índice `(OutfitId, SortOrder)`, FK a `Outfit` con `OnDelete(Cascade)` y FK a `Garment` con `OnDelete(Restrict)`. `Outfit.Items` como colección. Cambiar `Outfit.Source` para persistir `"ai"`/`"manual"` en minúsculas (el spec define esos valores) con un converter; no hay filas existentes.

- [ ] **Step 4: Implementar el listado**

`OutfitEndpoints.MapOutfitEndpoints` proyecta outfits con sus ítems y prendas (`AsNoTracking`), reutilizando `GarmentQuery.ToDto`. Filtrar prendas archivadas **no**: un outfit guardado conserva sus fotos. Registrar en `Program.cs` con `RequireToken()`.

- [ ] **Step 5: Correr el test y la suite**

Run: `dotnet test backend/FashionApp.sln --filter Lists_saved_outfits_with_their_garments`
Expected: PASS. Luego `dotnet test backend/FashionApp.sln`.

- [ ] **Step 6: Commit**

```bash
git add backend
git commit -m "feat: add outfit items model and outfit listing"
```

---

### Task 2: Reglas de composición y CRUD manual

**Files:**
- Create: `backend/FashionApp.Api/Features/Outfits/OutfitRules.cs`, `backend/FashionApp.Tests/OutfitRulesTests.cs`, `backend/FashionApp.Tests/OutfitEndpointTests.cs`
- Modify: `backend/FashionApp.Api/Features/Outfits/OutfitContracts.cs`, `backend/FashionApp.Api/Features/Outfits/OutfitEndpoints.cs`

**Interfaces:**
- Consumes: `Garment`, `GarmentCategory`, `OutfitItem`.
- Produces:
  - `record OutfitValidation(bool IsValid, string? Error);`
  - `static class OutfitRules { static OutfitValidation Validate(IReadOnlyCollection<Garment> garments); }`
  - `record CreateOutfitRequest(string Name, string? Occasion, Guid[] GarmentIds);`
  - `record PatchOutfitRequest(string? Name, string? Occasion, bool? IsFavorite, Guid[]? GarmentIds);`
  - `POST /api/outfits` → `201 OutfitDto`; `PATCH /api/outfits/{id}` → `200 OutfitDto`; `DELETE /api/outfits/{id}` → `204`.
  - Errores: `400` ProblemDetails para reglas/nombre; `404` si no existe; `409` si una prenda no existe o está archivada.

- [ ] **Step 1: Escribir los tests de reglas**

```csharp
[Theory]
[InlineData("top,bottom,shoes", true)]
[InlineData("dress,shoes", true)]
[InlineData("top,shoes", false)]                 // falta bottom
[InlineData("top,bottom", false)]                // falta shoes
[InlineData("top,top,bottom,shoes", false)]      // dos tops
[InlineData("top,bottom,shoes,outerwear,outerwear", false)]
public void Validates_category_composition(string csv, bool expected)
{
    var garments = csv.Split(',').Select(ToGarment).ToList();
    Assert.Equal(expected, OutfitRules.Validate(garments).IsValid);
}

[Fact]
public void Rejects_formality_spread_greater_than_two()
{
    var garments = new[] { ToGarment("top", 1), ToGarment("bottom", 4), ToGarment("shoes", 3) };
    Assert.False(OutfitRules.Validate(garments).IsValid);
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~OutfitRulesTests"`
Expected: FAIL de compilación.

- [ ] **Step 3: Implementar `OutfitRules`**

Reglas exactas del Global Constraints. Mensajes en español: `"El outfit necesita una parte superior y una inferior, o un vestido."`, `"El outfit necesita calzado."`, `"Solo puede haber un abrigo."`, `"No se puede repetir la misma prenda."`, `"La formalidad de las prendas no puede variar más de 2 puntos."`

- [ ] **Step 4: Implementar CRUD manual**

`POST`: valida nombre 1–120, carga prendas activas por ids (si falta o está archivada → `409`), valida reglas, crea `Outfit` + `OutfitItem` con `Layer` (0 base, 1 abrigo, 2 accesorios) y `SortOrder` por orden de entrada. `PATCH`: si vienen `GarmentIds`, reemplaza los ítems en una transacción; `IsFavorite` y nombre/ocasión se actualizan aparte. `DELETE`: borra ítems y outfit (cascade), nunca prendas.

- [ ] **Step 5: Correr los tests y la suite**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~Outfit"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend
git commit -m "feat: add outfit composition rules and manual CRUD"
```

---

### Task 3: Sugeridor por reglas y validación de propuestas

**Files:**
- Create: `backend/FashionApp.Api/Infrastructure/Ai/IOutfitSuggester.cs`, `backend/FashionApp.Api/Features/Outfits/OutfitSuggestionContracts.cs`, `backend/FashionApp.Api/Features/Outfits/OutfitSuggestionValidator.cs`, `backend/FashionApp.Api/Infrastructure/Ai/RuleBasedOutfitSuggester.cs`, `backend/FashionApp.Tests/OutfitSuggestionTests.cs`

**Interfaces:**
- Consumes: `Garment`, `OutfitRules`, `GarmentCategory`.
- Produces:
  - `record GarmentSummary(Guid Id, string Category, string ColorName, int Formality, string[] Seasons, string[] StyleTags);`
  - `record OutfitSuggestionContext(string? Occasion, string Weather, IReadOnlyList<GarmentSummary> Wardrobe);`
  - `record OutfitDraft(string Name, Guid[] GarmentIds, string? Occasion, string Rationale);`
  - `interface IOutfitSuggester { Task<IReadOnlyList<OutfitDraft>> SuggestAsync(OutfitSuggestionContext context, CancellationToken ct); }`
  - `static class OutfitSuggestionValidator { static IReadOnlyList<OutfitDraft> KeepValid(IReadOnlyList<OutfitDraft> drafts, IReadOnlyList<Garment> wardrobe); }`
  - `RuleBasedOutfitSuggester` que arma hasta 3 combinaciones respetando clima y formalidad.

- [ ] **Step 1: Escribir los tests**

```csharp
[Fact]
public void Drops_drafts_with_invented_ids_and_duplicate_categories()
{
    var wardrobe = new[] { Top(1), Bottom(2), Shoes(3) };
    var drafts = new[]
    {
        new OutfitDraft("Bien", new[] { wardrobe[0].Id, wardrobe[1].Id, wardrobe[2].Id }, null, "ok"),
        new OutfitDraft("Inventado", new[] { Guid.NewGuid() }, null, "mal"),
        new OutfitDraft("Dos tops", new[] { wardrobe[0].Id, Top(4).Id, wardrobe[2].Id }, null, "mal"),
    };

    var kept = OutfitSuggestionValidator.KeepValid(drafts, wardrobe);
    Assert.Single(kept);
    Assert.Equal("Bien", kept[0].Name);
}

[Fact]
public void Rule_based_suggester_returns_valid_outfits()
{
    var wardrobe = new[] { Top(2), Bottom(2), Shoes(2), Dress(3), Outerwear(2) };
    var drafts = new RuleBasedOutfitSuggester().SuggestAsync(
        new OutfitSuggestionContext("trabajo", "templado", wardrobe.Select(ToSummary).ToList()), default).Result;

    Assert.NotEmpty(drafts);
    Assert.All(drafts, d => Assert.True(OutfitRules.Validate(
        d.GarmentIds.Select(id => wardrobe.Single(g => g.Id == id)).ToList()).IsValid));
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~OutfitSuggestionTests"`
Expected: FAIL de compilación.

- [ ] **Step 3: Implementar el validador y el fallback**

`KeepValid` resuelve ids contra el guardarropa activo, descarta ids desconocidos y aplica `OutfitRules.Validate`; deduplica outfits por conjunto de ids. El fallback:
- filtra por temporada/clima (`calor` → `summer`, `frio` → `winter/autumn`, `templado` → todas);
- ordena por cercanía a la formalidad de la ocasión (trabajo 4, cena 4, casual 2, fiesta 3, deportivo 1);
- arma hasta 3 combinaciones: `dress+shoes`, `top+bottom+shoes`, más `outerwear` si el clima es frío;
- si no hay combinación válida, devuelve lista vacía (la UI muestra un mensaje, no un error).

- [ ] **Step 4: Correr los tests y la suite**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~OutfitSuggestionTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend
git commit -m "feat: add outfit suggestion validation and rule-based fallback"
```

---

### Task 4: Sugeridor IA, cuota y endpoint

**Files:**
- Create: `backend/FashionApp.Api/Infrastructure/Ai/FakeOutfitSuggester.cs`, `backend/FashionApp.Api/Infrastructure/Ai/GeminiOutfitSuggester.cs`, `backend/FashionApp.Api/Infrastructure/Ai/Prompts/outfit-suggestions.md`, `backend/FashionApp.Api/Features/Outfits/OutfitSuggestionService.cs`, `backend/FashionApp.Api/Features/Outfits/OutfitSuggestEndpoint.cs`, `backend/FashionApp.Tests/OutfitSuggestEndpointTests.cs`
- Modify: `backend/FashionApp.Api/Infrastructure/Quotas/AiQuotaService.cs`, `backend/FashionApp.Api/Program.cs`, `backend/FashionApp.Api/appsettings.json`

**Interfaces:**
- Consumes: `IOutfitSuggester`, `OutfitSuggestionValidator`, `RuleBasedOutfitSuggester`, `IAiQuota`, `IMemoryCache`.
- Produces:
  - `AiQuotaKinds.Suggestion = "suggestion"`.
  - `POST /api/outfits/suggest` body `{ occasion?: string, weather: "frio"|"templado"|"calor" }` → `{ outfits: OutfitDraft[] }`.
  - `IOutfitSuggestionService.SuggestAsync(string? occasion, string weather, CancellationToken ct)`.
  - Límite `Ai:DailySuggestionLimit` (default 20) para `suggestion`.
  - Caché en memoria por `occasion|weather|count|maxUpdatedAt` durante 10 minutos.

- [ ] **Step 1: Escribir los tests del endpoint**

```csharp
[Fact]
public async Task Suggest_returns_valid_outfits_and_consumes_suggestion_quota()
{
    await SeedWardrobe(factory, top: true, bottom: true, shoes: true);
    var response = await factory.CreateClient().PostAsJsonAsync("/api/outfits/suggest",
        new { occasion = "trabajo", weather = "templado" });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<SuggestOutfitsResponse>();
    Assert.NotEmpty(body!.Outfits);
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    Assert.Equal(1, (await db.UsageCounters.SingleAsync(c => c.Kind == "suggestion")).Count);
}

[Fact]
public async Task Suggest_returns_429_when_suggestion_quota_is_exhausted()
{
    // limit = 1, dos llamadas distintas (occasion distinta para evitar caché)
    var first = await PostSuggest(factory, "trabajo");
    var second = await PostSuggest(factory, "cena");
    Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    Assert.True(second.Headers.Contains("Retry-After"));
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~OutfitSuggestEndpointTests"`
Expected: FAIL con `404`.

- [ ] **Step 3: Implementar cuota por tipo**

`AiQuotaService` elige el límite según `kind`: `analysis` → `Ai:DailyAnalysisLimit` (50); `suggestion` → `Ai:DailySuggestionLimit` (20). `QuotaResults.TooManyRequests` recibe el `kind` y ajusta el mensaje ("sugerencias" / "análisis"). Mantener el upsert atómico.

- [ ] **Step 4: Implementar sugeridores**

`FakeOutfitSuggester` devuelve 2 borradores estables usando el guardarropa recibido. `GeminiOutfitSuggester`:
- prompt `outfit-suggestions.md` con ocasión, clima y lista compacta `{id, category, color, formality, seasons, styleTags}`;
- `responseMimeType=application/json`, temperatura 0.4, timeout 20 s;
- parsea `{ "outfits": [{ "name", "itemIds", "occasion", "rationale" }] }`;
- cualquier error → lista vacía (el servicio cae al fallback).

- [ ] **Step 5: Implementar el servicio y el endpoint**

`OutfitSuggestionService`:
1. normaliza clima (`frio`, `templado`, `calor`; inválido → `400`);
2. arma `OutfitSuggestionContext` con prendas activas;
3. consulta la caché; si hay resultado, lo devuelve **sin consumir cuota**;
4. en caso de miss, consume cuota `suggestion`; si no hay → `429`;
5. llama al sugeridor configurado; valida con `KeepValid`; si no queda ninguno, reintenta una vez y luego usa `RuleBasedOutfitSuggester`;
6. guarda en caché y devuelve.

Registrar `IMemoryCache`, `IOutfitSuggester` según `Ai:Provider` y el endpoint con `RequireToken()` + `BlockWritesInDemo()`.

`QuotaResults.TooManyRequests` pasa a recibir el `kind` para ajustar el mensaje; actualizar los llamadores existentes (`GarmentImageIntake`, `ReanalyzeEndpoint`) a `QuotaResults.TooManyRequests(context, AiQuotaKinds.Analysis)`.

- [ ] **Step 6: Correr los tests y la suite**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~OutfitSuggest"`
Expected: PASS. Luego la suite completa.

- [ ] **Step 7: Commit**

```bash
git add backend
git commit -m "feat: add AI outfit suggestions with quota and cache"
```

---

### Task 5: Frontend: tipos, API y listado en collage

**Files:**
- Create: `frontend/src/features/outfits/OutfitCard.tsx`, `frontend/src/features/outfits/OutfitCollage.tsx`, `frontend/src/features/outfits/outfitRules.ts`, `frontend/src/features/outfits/outfitRules.test.ts`
- Modify: `frontend/src/lib/types.ts`, `frontend/src/lib/api.ts`, `frontend/src/features/outfits/OutfitsPage.tsx`

**Interfaces:**
- Consumes: `GET /api/outfits`.
- Produces:
  - `Outfit`, `OutfitItem` en `types.ts`.
  - `api.getOutfits()`, `api.createOutfit(request)`, `api.patchOutfit(id, request)`, `api.deleteOutfit(id)`, `api.suggestOutfits(request)`.
  - `OutfitCollage` que muestra las fotos en una grilla estable (principal + secundarias) con `loading="lazy"` y `width/height`.
  - `OutfitsPage` con estado vacío, skeletons, error con reintento y lista de `OutfitCard` (nombre, ocasión, collage, favorito).

- [ ] **Step 1: Escribir el test que falla**

```ts
// outfitRules.test.ts
import { describe, expect, it } from 'vitest'
import { canSaveOutfit } from './outfitRules'

describe('canSaveOutfit', () => {
  it('requires shoes and a top+bottom or a dress', () => {
    expect(canSaveOutfit(['top', 'bottom'])).toBe(false)
    expect(canSaveOutfit(['top', 'bottom', 'shoes'])).toBe(true)
    expect(canSaveOutfit(['dress', 'shoes'])).toBe(true)
    expect(canSaveOutfit(['shoes'])).toBe(false)
  })
})
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `cd frontend && pnpm vitest run src/features/outfits/outfitRules.test.ts`
Expected: FAIL porque el módulo no existe.

- [ ] **Step 3: Implementar tipos, API y reglas de UI**

`outfitRules.ts` replica las reglas para deshabilitar el guardado y explicar el motivo, sin reemplazar la validación del servidor. `api.ts` sigue el patrón existente: `authHeaders`, `parseResponse`, `ApiError`.

- [ ] **Step 4: Implementar listado y collage**

`OutfitsPage` usa `useQuery(['outfits'], api.getOutfits)`. El collage muestra hasta 5 prendas con la primera más grande; si hay más, un contador "+N". Favorito con `PATCH`; eliminar con confirmación.

- [ ] **Step 5: Correr tests y build**

Run: `cd frontend && pnpm vitest run && pnpm build`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend
git commit -m "feat: add outfit listing with collage cards"
```

---

### Task 6: Constructor manual y edición

**Files:**
- Create: `frontend/src/features/outfits/OutfitBuilder.tsx`, `frontend/src/features/outfits/OutfitEditor.tsx`, `frontend/src/features/outfits/OutfitBuilder.test.tsx`
- Modify: `frontend/src/features/outfits/OutfitsPage.tsx`, `frontend/src/lib/api.ts`

**Interfaces:**
- Consumes: `api.getGarments` (paginado), `api.createOutfit`, `api.patchOutfit`.
- Produces:
  - Constructor con búsqueda/filtros por categoría, selección múltiple, preview del collage y validación en vivo.
  - Edición de un outfit existente (nombre, ocasión, prendas).
  - `data-testid="outfit-builder"` y botón "Guardar outfit" deshabilitado hasta que las reglas se cumplan.

- [ ] **Step 1: Escribir el test que falla**

```tsx
it('disables save until the selection is a valid outfit', async () => {
  render(<OutfitBuilder wardrobe={wardrobeFixture} onSave={vi.fn()} />)
  await userEvent.click(screen.getByRole('checkbox', { name: /camisa/i }))
  expect(screen.getByRole('button', { name: /guardar outfit/i })).toBeDisabled()
  await userEvent.click(screen.getByRole('checkbox', { name: /jean/i }))
  await userEvent.click(screen.getByRole('checkbox', { name: /zapatillas/i }))
  expect(screen.getByRole('button', { name: /guardar outfit/i })).toBeEnabled()
})
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `cd frontend && pnpm vitest run src/features/outfits/OutfitBuilder.test.tsx`
Expected: FAIL.

- [ ] **Step 3: Implementar el constructor**

Carga el guardarropa con páginas sucesivas (máx. 5 páginas / 120 prendas), agrupa por categoría, permite buscar por nombre y muestra el motivo cuando el outfit no es válido. Al guardar, `POST /api/outfits` y luego vuelve al listado con la query invalidada.

- [ ] **Step 4: Implementar la edición**

`OutfitEditor` precarga nombre, ocasión y prendas; guarda con `PATCH`. Reutiliza el constructor. No permite agregar prendas archivadas.

- [ ] **Step 5: Correr tests y build**

Run: `cd frontend && pnpm vitest run && pnpm build`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend
git commit -m "feat: add manual outfit builder and editor"
```

---

### Task 7: Panel de sugerencias, E2E y cierre

**Files:**
- Create: `frontend/src/features/outfits/SuggestionPanel.tsx`, `frontend/tests/outfits.spec.ts`
- Modify: `frontend/src/features/outfits/OutfitsPage.tsx`, `frontend/src/lib/api.ts`, `README.md`

**Interfaces:**
- Consumes: `api.suggestOutfits`, `api.createOutfit`, `api.getGarments`.
- Produces:
  - Panel con ocasión (casual, trabajo, cena, fiesta, deportivo) y clima (frío, templado, calor).
  - Tarjetas de propuesta con collage y razón; botón "Guardar" que crea el outfit con `source=ai`.
  - Estados: cargando, sin prendas suficientes, sin resultados, error y cuota agotada (`429` con mensaje claro).
  - E2E: sugerir → guardar → ver en el listado; y crear manualmente → editar nombre → ver el cambio.

- [ ] **Step 1: Implementar el panel**

Usa `useMutation` para sugerir. No persiste propuestas hasta que el usuario toca "Guardar". Deshabilita el formulario si el guardarropa no tiene al menos un outfit posible. Muestra `Retry-After` en minutos cuando la cuota está agotada.

- [ ] **Step 2: Escribir el E2E**

```ts
test('sugiere un outfit y lo guarda', async ({ page }) => {
  await seedWardrobe(page)                 // helper que confirma 3 prendas vía API
  await page.goto('/outfits')
  await page.getByRole('button', { name: 'Sugerir outfits' }).click()
  await page.getByLabel('Ocasión').selectOption('trabajo')
  await page.getByRole('button', { name: 'Generar' }).click()
  await expect(page.getByText('Outfit sugerido')).toBeVisible()
  await page.getByRole('button', { name: 'Guardar' }).first().click()
  await expect(page.getByText('Outfit sugerido')).toBeVisible()
})
```

- [ ] **Step 3: Correr el E2E y verificar que falla**

Run: `cd frontend && pnpm exec playwright test tests/outfits.spec.ts`
Expected: FAIL porque el panel no existe.

- [ ] **Step 4: Implementar y correr todo**

Run: `cd frontend && pnpm exec playwright test`, `pnpm vitest run`, `pnpm build`, y `dotnet test backend/FashionApp.sln`.
Expected: PASS.

- [ ] **Step 5: Documentar y commitear**

Actualizar `README.md` (endpoints de outfits, flujo, cuota de sugerencias) y la bitácora del spec. Commit:

```bash
git add frontend README.md
git commit -m "feat: complete outfit suggestions flow with E2E coverage"
```

---

## Siguientes planes

Al cerrar la Fase 2, el plan 3 cubre **Hoy** (RSS, resumen diario, relación con `StyleTags`) y el plan 4 el **portfolio** (demo desplegada, capturas, métricas).

> **Estado:** Fase 2 implementada y verificada el 2026-11-08 (192 tests backend, 46 frontend, 10 E2E).
