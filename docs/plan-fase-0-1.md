# Fashion App — Plan de implementación: Fase 0–1 (base + guardarropa)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dejar una app web funcionando donde se sube una foto de una prenda, se normaliza, se analiza con IA, se confirma y queda listada en el guardarropa con filtros.

**Architecture:** API .NET 10 Minimal APIs por features sobre SQLite, con un frontend Vite/React que consume JSON. La imagen se normaliza y guarda en disco; el análisis de IA queda detrás de una interfaz reemplazable por un fake en tests. La demo pública comparte el mismo código con `DEMO_MODE=true` y datos sembrados.

**Tech Stack:** .NET 10, EF Core 10 + SQLite, SixLabors.ImageSharp, xUnit + `WebApplicationFactory`, Vite + React 19 + TypeScript + Tailwind 4, TanStack Query 5, React Router 7, Vitest, Playwright.

**Spec:** `00 - Projects/Fashion App.md`

## Índice del plan

- [[#Global Constraints]]
- [[#Review Focus]]
- [[#File Structure]]
- [[#Task 1: Esqueleto del backend y health check]]
- [[#Task 2: Modelo de datos, EF Core y migración]]
- [[#Task 3: Lectura, edición y archivado de prendas]]
- [[#Task 4: Normalización de imágenes]]
- [[#Task 5: Alta de prenda con imagen y deduplicación]]
- [[#Task 6: Analizador de IA real y validación]]
- [[#Task 7: Reanálisis con confirmación]]
- [[#Task 8: Token, modo demo y seed]]
- [[#Task 9: Frontend: shell y guardarropa]]
- [[#Task 10: Alta desde la UI, E2E y cierre de fase]]

## Global Constraints

- .NET 10 y C# con las características del SDK instalado; no usar APIs de versiones anteriores sin verificar.
- Node 22+ y `pnpm`; versiones mayores de React 19, Vite 7, Tailwind 4, TanStack Query 5 y React Router 7.
- SQLite en `data/fashion.db`; uploads en `data/uploads`. Ambas rutas configurables y fuera de git.
- Todos los timestamps son `DateTimeOffset` en UTC; nunca hora local.
- Claves primarias `Guid` generadas en el servidor.
- Baja lógica únicamente en `Garment.IsArchived`; no hay borrado físico en esta fase.
- La UI y los mensajes de error van en español; los errores de API usan `ProblemDetails`.
- Token de la instancia personal en `App:Token` (env `APP__TOKEN`); demo con `Demo:Enabled` (env `DEMO__ENABLED=true`).
- Imágenes aceptadas: JPEG, PNG y WebP; máximo 10 MB; lado mayor 1600 px; miniatura 400 px en WebP; EXIF eliminado.
- Los tests nunca llaman a un proveedor de IA real. `Ai:Provider=fake` usa respuestas grabadas.
- Presupuesto de rendimiento: LCP < 2,5 s, INP < 200 ms, CLS < 0,1 en p75 móvil; lectura de API p95 < 300 ms.
- Prohibido N+1: toda lectura usa `AsNoTracking()` y proyección a DTO.
- Las imágenes se sirven desde `/media/{hash}.webp` con `Cache-Control: public, max-age=31536000, immutable`.

## Review Focus

1. **Archivo corrupto o formato no soportado** → `400`/`415` con mensaje claro, nunca `500` ni una prenda a medio guardar.
2. **Foto con rotación EXIF** (fotos de celular) → la prenda se ve derecha tanto en la miniatura como en el original normalizado.
3. **Mismo archivo subido dos veces** → una sola prenda, mismo `id`, sin segunda llamada de IA ni segundo archivo en disco.
4. **IA caída, timeout o JSON inválido** → la prenda se guarda igual con `Confidence = 0`, la respuesta es exitosa y el usuario puede reintentar.
5. **Token ausente o incorrecto en instancia personal** → `401` antes de tocar la base; en demo, toda escritura devuelve `403` sin ejecutarse.

---

## File Structure

```
fashion-app/
├─ backend/
│  ├─ FashionApp.sln
│  ├─ FashionApp.Api/
│  │  ├─ Program.cs
│  │  ├─ appsettings.json
│  │  ├─ Domain/
│  │  │  ├─ Garment.cs
│  │  │  ├─ GarmentCategory.cs
│  │  │  └─ Outfit.cs                  # entidades mínimas para la migración inicial
│  │  ├─ Infrastructure/
│  │  │  ├─ AppDbContext.cs
│  │  │  ├─ Storage/IImageNormalizer.cs
│  │  │  ├─ Storage/ImageSharpNormalizer.cs
│  │  │  ├─ Ai/IGarmentAnalyzer.cs
│  │  │  ├─ Ai/GarmentAnalysis.cs
│  │  │  ├─ Ai/FakeGarmentAnalyzer.cs
│  │  │  ├─ Ai/GeminiGarmentAnalyzer.cs
│  │  │  ├─ Ai/GarmentAnalysisValidator.cs
│  │  │  └─ Demo/DemoSeeder.cs
│  │  └─ Features/
│  │     ├─ Garments/GarmentEndpoints.cs
│  │     ├─ Garments/GarmentContracts.cs
│  │     ├─ Garments/GarmentQuery.cs
│  │     └─ Security/AccessFilters.cs
│  └─ FashionApp.Tests/
│     ├─ TestAppFactory.cs
│     ├─ HealthTests.cs
│     ├─ GarmentDataTests.cs
│     ├─ GarmentEndpointTests.cs
│     ├─ ImageNormalizerTests.cs
│     ├─ GarmentAnalysisTests.cs
│     └─ SecurityTests.cs
├─ frontend/
│  ├─ src/
│  │  ├─ main.tsx
│  │  ├─ App.tsx
│  │  ├─ lib/api.ts
│  │  ├─ lib/types.ts
│  │  ├─ features/wardrobe/WardrobePage.tsx
│  │  ├─ features/wardrobe/GarmentCard.tsx
│  │  ├─ features/wardrobe/GarmentFilters.tsx
│  │  ├─ features/wardrobe/UploadGarmentDialog.tsx
│  │  └─ features/wardrobe/garmentQueryString.ts
│  └─ tests/wardrobe.spec.ts
├─ data/                                # ignorado en git
└─ docker-compose.yml
```

---

### Task 1: Esqueleto del backend y health check

**Files:**
- Create: `backend/FashionApp.sln`, `backend/FashionApp.Api/FashionApp.Api.csproj`, `backend/FashionApp.Api/Program.cs`, `backend/FashionApp.Tests/FashionApp.Tests.csproj`, `backend/FashionApp.Tests/TestAppFactory.cs`, `backend/FashionApp.Tests/HealthTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `TestAppFactory : WebApplicationFactory<Program>`; endpoint `GET /api/health` que devuelve `200` con `{ "status": "ok", "utc": "<ISO-8601>" }`.

- [ ] **Step 1: Crear la solución y los proyectos**

```bash
dotnet new sln -n FashionApp -o backend
dotnet new web -n FashionApp.Api -o backend/FashionApp.Api
dotnet new xunit -n FashionApp.Tests -o backend/FashionApp.Tests
dotnet sln backend/FashionApp.sln add backend/FashionApp.Api backend/FashionApp.Tests
dotnet add backend/FashionApp.Tests reference backend/FashionApp.Api
dotnet add backend/FashionApp.Tests package Microsoft.AspNetCore.Mvc.Testing
```

- [ ] **Step 2: Escribir el test que falla**

```csharp
// backend/FashionApp.Tests/HealthTests.cs
public sealed class HealthTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task Health_returns_ok_and_utc()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.True(DateTimeOffset.TryParse(body.GetProperty("utc").GetString(), out _));
    }
}
```

```csharp
// backend/FashionApp.Tests/TestAppFactory.cs
public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    public string? Token { get; init; }
    public bool Demo { get; init; }
    public string UploadsPath { get; } =
        Path.Combine(Path.GetTempPath(), "fashion-tests", Guid.NewGuid().ToString("N"));
    public string DbPath => Path.Combine(UploadsPath, "test.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(UploadsPath);
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:Token"] = Token,
                ["Demo:Enabled"] = Demo.ToString(),
                ["Storage:Root"] = UploadsPath,
                ["ConnectionStrings:Default"] = $"Data Source={DbPath}"
            });
        });
    }
}
```

- [ ] **Step 3: Correr el test y verificar que falla**

Run: `dotnet test backend/FashionApp.sln --filter Health_returns_ok_and_utc`
Expected: FAIL porque `/api/health` devuelve `404`.

- [ ] **Step 4: Implementar el endpoint y exponer `Program`**

En `Program.cs`, al final: `public partial class Program;`. Registrar:

```csharp
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    utc = DateTimeOffset.UtcNow.ToString("O")
}));
```

Agregar `"Urls": "http://localhost:5210"` a `appsettings.Development.json` y CORS para el front:

```csharp
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(builder.Configuration["Cors:AllowedOrigin"] ?? "http://localhost:5173")
     .AllowAnyHeader().AllowAnyMethod()));
app.UseCors();
```

- [ ] **Step 5: Correr el test y verificar que pasa**

Run: `dotnet test backend/FashionApp.sln --filter Health_returns_ok_and_utc`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend
git commit -m "chore: scaffold .NET API with health endpoint"
```

---

### Task 2: Modelo de datos, EF Core y migración

**Files:**
- Create: `backend/FashionApp.Api/Domain/Garment.cs`, `backend/FashionApp.Api/Domain/GarmentCategory.cs`, `backend/FashionApp.Api/Domain/Outfit.cs`, `backend/FashionApp.Api/Infrastructure/AppDbContext.cs`, `backend/FashionApp.Tests/GarmentDataTests.cs`, `backend/FashionApp.Tests/TestData.cs`
- Modify: `backend/FashionApp.Api/Program.cs`, `backend/FashionApp.Api/appsettings.json`

**Interfaces:**
- Consumes: `Program` y `TestAppFactory` de Task 1.
- Produces:
  - `enum GarmentCategory { Top, Bottom, Dress, Outerwear, Shoes, Bag, Accessory }`
  - `sealed class Garment` con las propiedades del spec más `string ContentHash`, `string ImagePath`, `string ThumbPath`, `string? AnalysisJson`, `double Confidence`, `bool IsArchived`, `DateTimeOffset CreatedAt`, `DateTimeOffset UpdatedAt`.
  - `AppDbContext` con `DbSet<Garment> Garments`, `DbSet<Outfit> Outfits`.
  - Base SQLite creada con `Database.Migrate()` al arrancar.

- [ ] **Step 1: Escribir el test que falla**

```csharp
// backend/FashionApp.Tests/GarmentDataTests.cs
public sealed class GarmentDataTests
{
    [Fact]
    public async Task Can_persist_and_read_a_garment()
    {
        await using var db = TestDb.Create();
        db.Garments.Add(new Garment
        {
            Id = Guid.NewGuid(),
            Name = "Camisa de lino",
            Category = GarmentCategory.Top,
            ColorName = "beige",
            ColorHex = "#D8C3A5",
            Seasons = "spring,summer",
            Formality = 3,
            ContentHash = "abc",
            ImagePath = "abc.webp",
            ThumbPath = "abc-thumb.webp",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var saved = await db.Garments.AsNoTracking().SingleAsync();
        Assert.Equal("Camisa de lino", saved.Name);
        Assert.Equal(GarmentCategory.Top, saved.Category);
        Assert.False(saved.IsArchived);
    }
}
```

`TestData.cs` expone `TestDb.Create()` (contexto sobre el `DbPath` del factory con `Database.Migrate()`), `NewGarment(string name, GarmentCategory category, bool archived = false)` y `SeedOneGarment(TestAppFactory factory)` para los tests de este plan.

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test backend/FashionApp.sln --filter Can_persist_and_read_a_garment`
Expected: FAIL de compilación porque `Garment` y `AppDbContext` no existen.

- [ ] **Step 3: Implementar entidades y contexto**

Paquetes:

```bash
dotnet add backend/FashionApp.Api package Microsoft.EntityFrameworkCore.Sqlite
dotnet add backend/FashionApp.Api package Microsoft.EntityFrameworkCore.Design
dotnet tool install --global dotnet-ef
```

`Garment` con las propiedades del spec (`Name`, `Category`, `Subcategory`, `ColorName`, `ColorHex`, `SecondaryColorsJson`, `Pattern`, `Seasons`, `Formality`, `Material`, `Brand`, `StyleTagsJson`, `Notes`, `ImagePath`, `ThumbPath`, `ContentHash`, `AnalysisJson`, `Confidence`, `CreatedAt`, `UpdatedAt`, `IsArchived`).

`AppDbContext.OnModelCreating`:
- `Garment.Category` con `HasConversion<string>()` y `HasMaxLength(20)`.
- `Garment.ContentHash` único.
- Índices: `(IsArchived)`, `(Category)`, `(CreatedAt)`.
- `Outfit` con `Id`, `Name`, `Occasion`, `Source`, `Rationale`, `IsFavorite`, timestamps.

En `Program.cs`: `builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("Default")));` y antes de `app.Run()`:

```csharp
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
```

`appsettings.json`: `"ConnectionStrings": { "Default": "Data Source=../../data/fashion.db" }`.

- [ ] **Step 4: Crear la migración**

```bash
dotnet ef migrations add InitialCreate --project backend/FashionApp.Api
```

- [ ] **Step 5: Correr el test y verificar que pasa**

Run: `dotnet test backend/FashionApp.sln --filter Can_persist_and_read_a_garment`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend
git commit -m "feat: add garment domain model and SQLite migration"
```

---

### Task 3: Lectura, edición y archivado de prendas

**Files:**
- Modify: `backend/FashionApp.Tests/TestData.cs`
- Create: `backend/FashionApp.Api/Features/Garments/GarmentContracts.cs`, `backend/FashionApp.Api/Features/Garments/GarmentQuery.cs`, `backend/FashionApp.Api/Features/Garments/GarmentEndpoints.cs`, `backend/FashionApp.Tests/GarmentEndpointTests.cs`
- Modify: `backend/FashionApp.Api/Program.cs`

**Interfaces:**
- Consumes: `AppDbContext`, `Garment`, `GarmentCategory`.
- Produces:
  - `record GarmentDto(Guid Id, string Name, string Category, string ColorName, string ColorHex, int Formality, double Confidence, string[] Seasons, string[] StyleTags, string ImageUrl, string ThumbUrl);`
  - `record GarmentListResponse(IReadOnlyList<GarmentDto> Items, string? NextCursor);`
  - `record PatchGarmentRequest(string? Name, string? Category, string? ColorName, string? ColorHex, int? Formality, string[]? Seasons, string[]? StyleTags, string? Notes);`
  - Endpoints `GET /api/garments`, `PATCH /api/garments/{id}`, `DELETE /api/garments/{id}`.

- [ ] **Step 1: Escribir los tests que fallan**

```csharp
[Fact]
public async Task List_excludes_archived_and_filters_by_category()
{
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Garments.AddRange(
        NewGarment("Camisa", GarmentCategory.Top, archived: false),
        NewGarment("Jean", GarmentCategory.Bottom, archived: false),
        NewGarment("Viejo", GarmentCategory.Top, archived: true));
    await db.SaveChangesAsync();

    var body = await factory.CreateClient().GetFromJsonAsync<GarmentListResponse>("/api/garments?category=top");
    Assert.Single(body!.Items);
    Assert.Equal("Camisa", body.Items[0].Name);
}

[Fact]
public async Task Delete_archives_instead_of_removing()
{
    var id = await SeedOneGarment();
    var response = await factory.CreateClient().DeleteAsync($"/api/garments/{id}");
    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    Assert.True((await db.Garments.FindAsync(id))!.IsArchived);
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~GarmentEndpointTests"`
Expected: FAIL con `404`.

- [ ] **Step 3: Implementar contratos y consulta**

`GarmentQuery.Apply(IQueryable<Garment>, string? category, string? color, string? season, string? cursor)`:
- filtra `!IsArchived`;
- filtra categoría y color case-insensitive;
- filtra temporada con `EF.Functions.Like(g.Seasons, "%" + season + "%")`;
- ordena por `CreatedAt DESC, Id DESC`;
- cursor opaco: base64 de `"{CreatedAt.UtcTicks}:{Id}"`, aplica `(CreatedAt, Id) < cursor`;
- `Take(24)` y `NextCursor` si hay más.

`ToDto` construye URLs `/media/{ContentHash}.webp` y `/media/{ContentHash}-thumb.webp`.

- [ ] **Step 4: Registrar los endpoints**

`MapGroup("/api/garments")` con `GET`, `PATCH` (valida categoría y formality 1–5; devuelve `400` si no existe o es inválido) y `DELETE` (setea `IsArchived = true`, `UpdatedAt`; devuelve `204`; si no existe, `404`).

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~GarmentEndpointTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend
git commit -m "feat: add garment list, patch and archive endpoints"
```

---

### Task 4: Normalización de imágenes

**Files:**
- Create: `backend/FashionApp.Api/Infrastructure/Storage/IImageNormalizer.cs`, `backend/FashionApp.Api/Infrastructure/Storage/ImageSharpNormalizer.cs`, `backend/FashionApp.Tests/ImageNormalizerTests.cs`, `backend/FashionApp.Tests/TestImages.cs`
- Modify: `backend/FashionApp.Api/FashionApp.Api.csproj`

**Interfaces:**
- Consumes: nada.
- Produces:
  - `sealed record NormalizedImage(byte[] Original, byte[] Thumbnail, string ContentHash, int Width, int Height);`
  - `interface IImageNormalizer { Task<NormalizedImage> NormalizeAsync(Stream input, CancellationToken ct); }`
  - `sealed class InvalidImageException(string message) : Exception(message);`

- [ ] **Step 1: Agregar ImageSharp**

```bash
dotnet add backend/FashionApp.Api package SixLabors.ImageSharp
```

- [ ] **Step 2: Escribir los tests que fallan**

```csharp
[Fact]
public async Task Normalizes_to_max_1600_and_creates_400_thumbnail()
{
    var input = TestImages.CreateJpeg(2400, 1200, ExifOrientation.Rotate90);
    var result = await new ImageSharpNormalizer().NormalizeAsync(input, default);

    using var original = Image.Load(result.Original);
    using var thumb = Image.Load(result.Thumbnail);
    Assert.Equal(1200, original.Width);
    Assert.Equal(2400, original.Height);          // rotación EXIF aplicada
    Assert.True(Math.Max(thumb.Width, thumb.Height) <= 400);
    Assert.Equal(64, result.ContentHash.Length);
}

[Fact]
public async Task Rejects_corrupt_input()
{
    using var trash = new MemoryStream([1, 2, 3, 4, 5]);
    await Assert.ThrowsAsync<InvalidImageException>(
        () => new ImageSharpNormalizer().NormalizeAsync(trash, default));
}
```

`TestImages.CreateJpeg` usa ImageSharp para dibujar un rectángulo y escribir el EXIF de orientación. `TestImages.Multipart(name, bytes)` arma un `MultipartFormDataContent` con el campo `file` y el `Content-Type` correcto.

- [ ] **Step 3: Correr los tests y verificar que fallan**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~ImageNormalizerTests"`
Expected: FAIL de compilación.

- [ ] **Step 4: Implementar el normalizador**

`ImageSharpNormalizer`:
1. `Image.LoadAsync(input)` dentro de try/catch; cualquier excepción → `InvalidImageException("La imagen no se pudo leer.")`.
2. `image.Mutate(x => x.AutoOrient())`.
3. Redimensionar si el lado mayor supera 1600, con `ResizeMode.Max`.
4. Codificar a WebP con calidad 82 en `MemoryStream`.
5. Miniatura de 400 px (lado mayor) y WebP calidad 75.
6. `ContentHash = Convert.ToHexString(SHA256.HashData(originalBytes)).ToLowerInvariant()`.
7. Devolver dimensiones finales.

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~ImageNormalizerTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend
git commit -m "feat: normalize uploaded images and strip metadata"
```

---

### Task 5: Alta de prenda con imagen y deduplicación

**Files:**
- Create: `backend/FashionApp.Api/Infrastructure/Storage/FileStorage.cs`, `backend/FashionApp.Api/Infrastructure/Ai/GarmentAnalysis.cs`, `backend/FashionApp.Api/Infrastructure/Ai/IGarmentAnalyzer.cs`, `backend/FashionApp.Api/Infrastructure/Ai/FakeGarmentAnalyzer.cs`, `backend/FashionApp.Api/Features/Garments/GarmentUploadEndpoint.cs`, `backend/FashionApp.Tests/GarmentUploadTests.cs`
- Modify: `backend/FashionApp.Api/Program.cs`, `backend/FashionApp.Api/appsettings.json`, `backend/FashionApp.Tests/TestData.cs`

**Interfaces:**
- Consumes: `IImageNormalizer`, `NormalizedImage`, `AppDbContext`, `Garment`.
- Produces:
  - `sealed record GarmentAnalysis(string Name, string Category, string? Subcategory, string[] Colors, string? Pattern, string[] Seasons, int Formality, string? Material, string[] StyleTags, double Confidence);` con `static GarmentAnalysis Empty()`.
  - `interface IGarmentAnalyzer { Task<GarmentAnalysis> AnalyzeAsync(byte[] image, string contentType, CancellationToken ct); }`
  - `record CreateGarmentResponse(GarmentDto Garment, bool Deduplicated);`
  - `POST /api/garments` (multipart, campo `file`) → `201` con `CreateGarmentResponse`; duplicado → `200` con el existente.
  - `GET /media/{hash}-thumb.webp` y `/media/{hash}.webp` con caché inmutable.
  - `FakeGarmentAnalyzer` registrado cuando `Ai:Provider = "fake"` o está ausente.

- [ ] **Step 1: Escribir los tests que fallan**

```csharp
[Fact]
public async Task Upload_creates_garment_and_writes_two_files()
{
    var client = factory.CreateClient();
    using var content = TestImages.Multipart("camisa.jpg", TestImages.CreateJpeg(800, 600));
    var response = await client.PostAsync("/api/garments", content);

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<CreateGarmentResponse>();
    Assert.False(body!.Deduplicated);
    Assert.Equal(2, Directory.GetFiles(factory.UploadsPath, "*.webp").Length);
}

[Fact]
public async Task Uploading_same_file_twice_returns_existing_id()
{
    var client = factory.CreateClient();
    var bytes = TestImages.CreateJpeg(800, 600);
    var first = await client.PostAsync("/api/garments", TestImages.Multipart("a.jpg", bytes));
    var second = await client.PostAsync("/api/garments", TestImages.Multipart("b.jpg", bytes));

    Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    var a = await first.Content.ReadFromJsonAsync<CreateGarmentResponse>();
    var b = await second.Content.ReadFromJsonAsync<CreateGarmentResponse>();
    Assert.Equal(a!.Garment.Id, b!.Garment.Id);
}

[Fact]
public async Task Corrupt_file_returns_400_and_persists_nothing()
{
    var client = factory.CreateClient();
    var response = await client.PostAsync("/api/garments",
        TestImages.Multipart("x.jpg", new byte[] { 1, 2, 3 }));
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var list = await factory.CreateClient().GetFromJsonAsync<GarmentListResponse>("/api/garments");
    Assert.Empty(list!.Items);
}

[Fact]
public async Task Analyzer_failure_still_saves_garment_with_zero_confidence()
{
    using var failing = factory.WithWebHostBuilder(builder =>
        builder.ConfigureServices(services =>
            services.AddSingleton<IGarmentAnalyzer, ThrowingAnalyzer>()));

    var response = await failing.CreateClient().PostAsync("/api/garments",
        TestImages.Multipart("ok.jpg", TestImages.CreateJpeg(600, 600)));

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<CreateGarmentResponse>();
    Assert.Equal(0, body!.Garment.Confidence);
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~GarmentUploadTests"`
Expected: FAIL con `404`.

- [ ] **Step 3: Implementar almacenamiento y endpoint**

`FileStorage` escribe `{hash}.webp` y `{hash}-thumb.webp`, crea el directorio si no existe y no sobrescribe archivos existentes.

Endpoint:
- valida `file.Length <= 10 MB` → `413`;
- valida extensión y contenido real por magic bytes (JPEG `FF D8 FF`, PNG `89 50 4E 47`, WebP `RIFF....WEBP`) → `415`;
- normaliza; si `InvalidImageException` → `ProblemDetails` `400`;
- busca `Garment` por `ContentHash`; si existe devuelve `200` con `Deduplicated = true`;
- llama a `IGarmentAnalyzer`; cualquier excepción se captura y se usa `GarmentAnalysis.Empty()` con `Confidence = 0`;
- mapea el análisis a la entidad, guarda y devuelve `201`.

`FakeGarmentAnalyzer` devuelve una camisa beige fija y se registra con `Ai:Provider = "fake"`. `ThrowingAnalyzer` (en `TestData.cs`) implementa `IGarmentAnalyzer` y lanza `HttpRequestException`; se usa para el caso de IA caída.

- [ ] **Step 4: Servir media con caché**

```csharp
app.MapGet("/media/{fileName}", (string fileName, HttpContext ctx, IConfiguration config) =>
{
    if (!Regex.IsMatch(fileName, @"^[a-f0-9]{64}(-thumb)?\.webp$"))
        return Results.NotFound();
    var path = Path.Combine(config["Storage:Root"] ?? "data/uploads", fileName);
    if (!File.Exists(path)) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    return Results.File(path, "image/webp", enableRangeProcessing: true);
});
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~GarmentUploadTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend
git commit -m "feat: upload garments with image normalization and deduplication"
```

---

### Task 6: Analizador de IA real y validación

**Files:**
- Create: `backend/FashionApp.Api/Infrastructure/Ai/GeminiGarmentAnalyzer.cs`, `backend/FashionApp.Api/Infrastructure/Ai/GarmentAnalysisValidator.cs`, `backend/FashionApp.Api/Infrastructure/Ai/Prompts/garment-analysis.md`, `backend/FashionApp.Tests/GarmentAnalysisTests.cs`, `backend/FashionApp.Tests/StubHttpHandler.cs`
- Modify: `backend/FashionApp.Api/Program.cs`, `backend/FashionApp.Api/appsettings.json`

**Interfaces:**
- Consumes: `IGarmentAnalyzer`, `GarmentAnalysis` y `FakeGarmentAnalyzer` de Task 5.
- Produces:
  - `sealed class AiOptions { public string Provider { get; set; } = "fake"; public string ApiKey { get; set; } = ""; public string Model { get; set; } = "gemini-2.0-flash"; }`
  - `GarmentAnalysisValidator.TryNormalize(GarmentAnalysis input, out GarmentAnalysis normalized)` que corrige categoría inválida a `top`, formality a `1..5`, descarta style tags fuera del vocabulario y recorta longitudes.
  - `GeminiGarmentAnalyzer : IGarmentAnalyzer` con `HttpClient` inyectado y `IOptions<AiOptions>`.

- [ ] **Step 1: Escribir los tests que fallan**

```csharp
[Fact]
public void Validator_discards_unknown_tags_and_clamps_formality()
{
    var raw = new GarmentAnalysis("Camisa", "top", null,
        ["beige"], null, ["summer"], 9, null, ["minimalista", "inventado"], 0.8);

    Assert.True(GarmentAnalysisValidator.TryNormalize(raw, out var clean));
    Assert.Equal(5, clean.Formality);
    Assert.Equal(new[] { "minimalista" }, clean.StyleTags);
}

[Fact]
public async Task Gemini_analyzer_parses_valid_json()
{
    var handler = new StubHttpHandler("""
        {"candidates":[{"content":{"parts":[{"text":"{\\"name\\":\\"Camisa\\",\\"category\\":\\"top\\",\\"colors\\":[{\\"name\\":\\"beige\\",\\"hex\\":\\"#D8C3A5\\"}],\\"seasons\\":[\\"summer\\"],\\"formality\\":3,\\"styleTags\\":[\\"minimalista\\"],\\"confidence\\":0.9}"}]}}]}
        """);
    var analyzer = new GeminiGarmentAnalyzer(new HttpClient(handler), Options.Create(new AiOptions { ApiKey = "test" }));

    var result = await analyzer.AnalyzeAsync([1, 2, 3], "image/jpeg", default);
    Assert.Equal("Camisa", result.Name);
    Assert.Equal(0.9, result.Confidence);
}

[Fact]
public async Task Gemini_analyzer_returns_empty_analysis_on_garbage()
{
    var handler = new StubHttpHandler("""{"candidates":[{"content":{"parts":[{"text":"no soy json"}]}}]}""");
    var analyzer = new GeminiGarmentAnalyzer(new HttpClient(handler), Options.Create(new AiOptions { ApiKey = "test" }));
    var result = await analyzer.AnalyzeAsync([1, 2, 3], "image/jpeg", default);
    Assert.Equal(0, result.Confidence);
}
```

`StubHttpHandler` es un `HttpMessageHandler` que devuelve como `application/json` el string que recibe en el constructor; implementa `SendAsync` y descarta el request.

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~GarmentAnalysisTests"`
Expected: FAIL de compilación.

- [ ] **Step 3: Implementar el validador y el analizador**

`GarmentAnalysisValidator`:
- categorías válidas: `top, bottom, dress, outerwear, shoes, bag, accessory`;
- `seasons` ⊆ `spring, summer, autumn, winter`;
- `styleTags` ⊆ las 18 etiquetas del spec ([[Fashion App#Vocabulario controlado de estilo]]);
- formality `Math.Clamp(1, 5)`;
- strings recortados a las longitudes del modelo; si `Name` queda vacío → `"Prenda"`.

`GeminiGarmentAnalyzer`:
- `POST https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={ApiKey}`;
- body con `contents[0].parts[0].text` = prompt + `inline_data` base64 de la imagen;
- `generationConfig.responseMimeType = "application/json"` y `temperature = 0`;
- timeout de 20 s vía `HttpClient.Timeout`;
- extrae `candidates[0].content.parts[0].text`, parsea con `System.Text.Json` (case-insensitive) y valida;
- cualquier excepción o JSON inválido devuelve `GarmentAnalysis.Empty()`.

El prompt vive en `Prompts/garment-analysis.md`, se lee embebido y enumera categorías, temporadas y las 18 etiquetas.

- [ ] **Step 4: Cambiar el registro por proveedor**

```csharp
if (config["Ai:Provider"] == "gemini")
    builder.Services.AddHttpClient<IGarmentAnalyzer, GeminiGarmentAnalyzer>();
else
    builder.Services.AddSingleton<IGarmentAnalyzer, FakeGarmentAnalyzer>();
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~GarmentAnalysisTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend
git commit -m "feat: add Gemini garment analyzer with strict validation"
```

---

### Task 7: Reanálisis con confirmación

**Files:**
- Create: `backend/FashionApp.Api/Features/Garments/ReanalyzeEndpoint.cs`, `backend/FashionApp.Tests/ReanalyzeTests.cs`
- Modify: `backend/FashionApp.Api/Features/Garments/GarmentEndpoints.cs`, `backend/FashionApp.Api/Program.cs`, `backend/FashionApp.Tests/TestData.cs`

**Interfaces:**
- Consumes: `IGarmentAnalyzer`, `GarmentAnalysisValidator`, `AppDbContext`, `IConfiguration`.
- Produces: `POST /api/garments/{id}/reanalyze` → `200` con `GarmentAnalysis`; `404` si no existe; **no modifica la base**.

- [ ] **Step 1: Escribir el test que falla**

```csharp
[Fact]
public async Task Reanalyze_returns_proposal_without_changing_garment()
{
    var id = await SeedGarmentWithImage(factory, "Camisa vieja");
    var response = await factory.CreateClient().PostAsync($"/api/garments/{id}/reanalyze", null);
    var proposal = await response.Content.ReadFromJsonAsync<GarmentAnalysis>();

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("Camisa de lino", proposal!.Name);       // lo que devuelve el fake
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    Assert.Equal("Camisa vieja", (await db.Garments.FindAsync(id))!.Name);
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test backend/FashionApp.sln --filter Reanalyze_returns_proposal_without_changing_garment`
Expected: FAIL con `404`.

- [ ] **Step 3: Implementar el endpoint**

Agregar a `TestData.cs` el helper `SeedGarmentWithImage(TestAppFactory factory, string name)`: normaliza una imagen de `TestImages`, escribe los archivos en `factory.UploadsPath`, crea el `Garment` con ese `ContentHash` y devuelve el `Id`.

El endpoint lee el archivo `{ImagePath}` desde `Storage:Root`; si no existe devuelve `409` con `ProblemDetails`. Llama al analizador, valida y devuelve la propuesta. No toca `Garment`.

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test backend/FashionApp.sln --filter Reanalyze_returns_proposal_without_changing_garment`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend
git commit -m "feat: add non-destructive garment reanalysis"
```

---

### Task 8: Token, modo demo y seed

**Files:**
- Create: `backend/FashionApp.Api/Features/Security/AccessFilters.cs`, `backend/FashionApp.Api/Infrastructure/Demo/DemoSeeder.cs`, `backend/FashionApp.Api/Infrastructure/Quotas/AiQuotaService.cs`, `backend/FashionApp.Api/Domain/UsageCounter.cs`, `backend/FashionApp.Tests/SecurityTests.cs`
- Modify: `backend/FashionApp.Api/Program.cs`, `backend/FashionApp.Api/appsettings.json`, `backend/FashionApp.Api/Infrastructure/AppDbContext.cs`, `backend/FashionApp.Api/Features/Garments/GarmentUploadEndpoint.cs`
- Migration: `dotnet ef migrations add AddUsageCounters --project backend/FashionApp.Api`
- Assets: `backend/FashionApp.Api/Infrastructure/Demo/SeedAssets/*.webp` (12–15 fotos de prendas de stock con licencia libre).

**Interfaces:**
- Consumes: `AppDbContext`, endpoints de Tasks 3, 5 y 7.
- Produces:
  - `sealed record AppSecurityOptions(string Token, bool DemoEnabled);`
  - Filtro `RequireToken` que devuelve `401` si falta o no coincide con `App:Token`.
  - Filtro `BlockWritesInDemo` que devuelve `403` en `POST/PATCH/DELETE` cuando `Demo:Enabled`.
  - `interface IAiQuota { Task<bool> TryConsumeAsync(string kind, CancellationToken ct); }` con `kind = "analysis"`; lee `Ai:DailyAnalysisLimit` (default 50).
  - `DemoSeeder.SeedAsync(AppDbContext db)` idempotente: si no hay prendas, inserta las 12–15 del set con `ContentHash` y archivos ya copiados a uploads.

- [ ] **Step 1: Escribir los tests que fallan**

```csharp
[Fact]
public async Task Personal_instance_requires_token()
{
    using var factory = new TestAppFactory { Token = "secreto", Demo = false };
    var client = factory.CreateClient();
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/garments")).StatusCode);

    client.DefaultRequestHeaders.Add("X-App-Token", "secreto");
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/garments")).StatusCode);
}

[Fact]
public async Task Demo_instance_blocks_writes_but_allows_reads()
{
    using var factory = new TestAppFactory { Demo = true };
    var client = factory.CreateClient();
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/garments")).StatusCode);

    using var content = TestImages.Multipart("a.jpg", TestImages.CreateJpeg(200, 200));
    Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/garments", content)).StatusCode);
}

[Fact]
public async Task Demo_seed_is_idempotent()
{
    using var factory = new TestAppFactory { Demo = true };
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var first = await db.Garments.CountAsync();
    await DemoSeeder.SeedAsync(db);
    Assert.Equal(first, await db.Garments.CountAsync());
    Assert.InRange(first, 12, 15);
}

[Fact]
public async Task Analysis_quota_returns_429_when_exhausted()
{
    using var factory = new TestAppFactory();
    using var limited = factory.WithWebHostBuilder(builder =>
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
                { ["Ai:DailyAnalysisLimit"] = "1" })));
    var client = limited.CreateClient();

    var first = await client.PostAsync("/api/garments",
        TestImages.Multipart("a.jpg", TestImages.CreateJpeg(600, 600)));
    var second = await client.PostAsync("/api/garments",
        TestImages.Multipart("b.jpg", TestImages.CreateJpeg(601, 601)));

    Assert.Equal(HttpStatusCode.Created, first.StatusCode);
    Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~SecurityTests"`
Expected: FAIL.

- [ ] **Step 3: Implementar filtros**

`AccessFilters.RequireToken` lee `App:Token`; si está vacío, no exige token (entorno de tests existente). Compara con `CryptographicOperations.FixedTimeEquals` sobre UTF-8.

`AccessFilters.BlockWritesInDemo` se agrega a los endpoints de escritura con `AddEndpointFilter`.

- [ ] **Step 4: Implementar la cuota diaria**

`UsageCounter` tiene PK compuesta `(Date, Kind)` y `Count`. `AiQuotaService.TryConsumeAsync` abre una transacción, busca el contador del día UTC, y si `Count >= limit` devuelve `false`; si no, incrementa y guarda. El endpoint de alta y el de reanálisis lo llaman antes de invocar a la IA; si devuelve `false`, responden `429` con `Retry-After` hasta la medianoche UTC. La demo no consume cuota porque no llama a la IA.

- [ ] **Step 5: Implementar seed**

Lee `SeedAssets/manifest.json` con nombre, categoría, color, temporada, formality, styleTags y archivo. Copia los `.webp` a uploads con el hash de su contenido y crea los `Garment` con `Confidence = 1`. No llama a la IA. Se ejecuta al arrancar solo si `Demo:Enabled` y la tabla está vacía.

- [ ] **Step 6: Correr los tests y verificar que pasan**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~SecurityTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend
git commit -m "feat: add token auth, demo write guard, AI quota and seeded wardrobe"
```

---

### Task 9: Frontend: shell y guardarropa

**Files:**
- Create: `frontend/` (Vite), `frontend/src/main.tsx`, `frontend/src/App.tsx`, `frontend/src/lib/api.ts`, `frontend/src/lib/types.ts`, `frontend/src/features/wardrobe/WardrobePage.tsx`, `GarmentCard.tsx`, `GarmentFilters.tsx`, `garmentQueryString.ts`, `frontend/src/features/wardrobe/garmentQueryString.test.ts`
- Modify: `frontend/index.html`, `frontend/vite.config.ts`

**Interfaces:**
- Consumes: `GET /api/garments` y tipos de Task 3.
- Produces:
  - `buildGarmentQuery(filters: { category?: string; color?: string; season?: string }): string` → `"?category=top&season=summer"` sin claves vacías.
  - `api.getGarments(query)`, `api.postGarment(file)` y `api.reanalyze(id)` con `X-App-Token` desde `sessionStorage` (`app_token`).
  - Rutas `/` (Hoy placeholder), `/guardarropa`, `/outfits` (placeholder).

- [ ] **Step 1: Scaffold y dependencias**

```bash
pnpm create vite frontend --template react-ts
cd frontend
pnpm add @tanstack/react-query react-router-dom
pnpm add -D tailwindcss @tailwindcss/vite vitest @testing-library/react @testing-library/jest-dom jsdom
```

Configurar Tailwind 4 en `vite.config.ts` con `@tailwindcss/vite` y `@import "tailwindcss";` en `index.css`.

- [ ] **Step 2: Escribir el test que falla**

```ts
// garmentQueryString.test.ts
import { describe, expect, it } from 'vitest'
import { buildGarmentQuery } from './garmentQueryString'

describe('buildGarmentQuery', () => {
  it('omits empty filters and encodes values', () => {
    expect(buildGarmentQuery({ category: 'top', color: '', season: 'summer' }))
      .toBe('?category=top&season=summer')
  })
})
```

- [ ] **Step 3: Correr el test y verificar que falla**

Run: `cd frontend && pnpm vitest run src/features/wardrobe/garmentQueryString.test.ts`
Expected: FAIL porque el módulo no existe.

- [ ] **Step 4: Implementar el cliente y la vista**

`api.ts`: `fetch` con `baseURL = import.meta.env.VITE_API_URL ?? 'http://localhost:5210'`, header `X-App-Token`, parseo de `ProblemDetails` y error tipado.

`WardrobePage`: `useQuery(['garments', filters], …)` con `keepPreviousData`; grilla responsive (`grid-cols-2 md:grid-cols-4`); `GarmentCard` con `<img loading="lazy" width={400} height={400}>`; filtros por categoría y temporada; skeletons con la misma altura que las tarjetas; estado vacío que invita a subir la primera prenda.

- [ ] **Step 5: Correr el test y el build**

Run: `cd frontend && pnpm vitest run && pnpm build`
Expected: PASS y build sin warnings de tamaño.

- [ ] **Step 6: Commit**

```bash
git add frontend
git commit -m "feat: add responsive wardrobe grid with filters"
```

---

### Task 10: Alta desde la UI, E2E y cierre de fase

**Files:**
- Create: `frontend/src/features/wardrobe/UploadGarmentDialog.tsx`, `frontend/src/features/wardrobe/GarmentForm.tsx`, `frontend/tests/wardrobe.spec.ts`, `frontend/tests/fixtures/camisa.jpg`, `README.md`
- Modify: `frontend/src/features/wardrobe/WardrobePage.tsx`, `backend/FashionApp.Api/Program.cs`

**Interfaces:**
- Consumes: `api.postGarment`, `api.reanalyze`, `GarmentAnalysis` y `CreateGarmentResponse`.
- Produces: flujo completo subir → previsualizar → confirmar → ver en grilla; comando documentado para correr backend y frontend; README con decisiones y capturas pendientes.

- [ ] **Step 1: Implementar el diálogo de alta**

Input `accept="image/jpeg,image/png,image/webp"`, preview local con `URL.createObjectURL`, validación de 10 MB en cliente con mensaje en español, spinner con texto "Analizando prenda…", y formulario de confirmación con los campos del análisis (nombre, categoría, color, temporada, formalidad, etiquetas). El botón "Guardar" hace `POST /api/garments`; el botón "Reintentar" hace `POST /{id}/reanalyze` y vuelve a llenar el formulario sin guardar.

- [ ] **Step 2: Escribir el E2E que falla**

```ts
// frontend/tests/wardrobe.spec.ts
test('sube una prenda y la ve en el guardarropa', async ({ page }) => {
  await page.goto('/guardarropa')
  await page.getByRole('button', { name: 'Subir prenda' }).click()
  await page.setInputFiles('input[type=file]', 'tests/fixtures/camisa.jpg')
  await expect(page.getByText('Analizando prenda…')).toBeVisible()
  await page.getByLabel('Nombre').fill('Camisa de lino')
  await page.getByRole('button', { name: 'Guardar' }).click()
  await expect(page.getByText('Camisa de lino')).toBeVisible()
})
```

- [ ] **Step 3: Correr el E2E y verificar que falla**

Run: `cd frontend && pnpm exec playwright test`
Expected: FAIL porque no existe el diálogo.

- [ ] **Step 4: Levantar backend en modo fake y correr el E2E**

En una terminal:

```bash
Ai__Provider=fake dotnet run --project backend/FashionApp.Api
```

En otra terminal:

```bash
cd frontend && VITE_API_URL=http://localhost:5210 pnpm dev
```

Run: `cd frontend && pnpm exec playwright test`
Expected: PASS.

- [ ] **Step 5: Verificar rendimiento y documentar**

- `pnpm build` y revisar que el JS inicial no supere 200 KB gzip.
- Lighthouse móvil sobre `/guardarropa`: LCP < 2,5 s, CLS < 0,1.
- `README.md` con: requisitos, cómo correr backend y frontend, variables de entorno, cómo sembrar la demo, y una tabla de endpoints.
- Anotar en la bitácora del spec que la Fase 1 quedó cerrada.

- [ ] **Step 6: Commit**

```bash
git add frontend README.md
git commit -m "feat: complete wardrobe upload flow with E2E coverage"
```

---

## Siguientes planes

Este plan cierra la **Fase 0–1**. Los siguientes se escriben al terminar, con las interfaces ya reales:

1. **Plan 2 — Outfits**: sugerencia IA, validación de composición, editor manual, collage.
2. **Plan 3 — Hoy**: job RSS, resumen diario, intersección con `StyleTags`, caché.
3. **Plan 4 — Portfolio**: modo demo desplegado, métricas, capturas y README final.

Cada plan debe entregar software funcionando y testeable por sí solo.
