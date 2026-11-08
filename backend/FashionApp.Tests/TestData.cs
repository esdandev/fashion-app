using FashionApp.Api.Domain;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Ai;
using FashionApp.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Deterministic clock for components that take a <see cref="TimeProvider"/>.
/// Tests call <see cref="Advance"/> instead of sleeping.
/// </summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    // Keep "server local time" deterministic in tests regardless of the
    // machine's time zone.
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}

/// <summary>
/// Lets a test advance a clock exposed as <see cref="TimeProvider"/> without
/// production code depending on the test double.
/// </summary>
public static class TimeProviderTestExtensions
{
    public static void Advance(this TimeProvider provider, TimeSpan delta)
    {
        if (provider is not ManualTimeProvider manual)
            throw new InvalidOperationException("El test requiere un ManualTimeProvider.");

        manual.Advance(delta);
    }
}

public static class TestDb
{
    public static AppDbContext Create(string? dbPath = null)
    {
        dbPath ??= Path.Combine(
            Path.GetTempPath(), "fashion-tests", Guid.NewGuid().ToString("N"), "test.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new AppDbContext(options);
        db.Database.Migrate();
        return db;
    }
}

public static class TestData
{
    public static Garment NewGarment(string name, GarmentCategory category, bool archived = false)
    {
        var hash = Guid.NewGuid().ToString("N");
        return new Garment
        {
            Id = Guid.NewGuid(),
            Name = name,
            Category = category,
            ColorName = "beige",
            ColorHex = "#D8C3A5",
            Seasons = "spring,summer",
            Formality = 3,
            ContentHash = hash,
            ImagePath = $"{hash}.webp",
            ThumbPath = $"{hash}-thumb.webp",
            IsArchived = archived,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public static async Task<Guid> SeedOneGarment(TestAppFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var garment = NewGarment("Camisa de lino", GarmentCategory.Top);
        db.Garments.Add(garment);
        await db.SaveChangesAsync();
        return garment.Id;
    }

    /// <summary>
    /// Seeds a garment whose normalized image and thumbnail are present in the
    /// factory storage root, so endpoints that read the file can be exercised
    /// without going through the upload endpoint.
    /// </summary>
    public static async Task<Guid> SeedGarmentWithImage(TestAppFactory factory, string name)
    {
        using var source = TestImages.CreateJpeg(640, 480);
        var normalized = await new MagickImageNormalizer().NormalizeAsync(source, default);

        var imagePath = $"{normalized.ContentHash}.webp";
        var thumbPath = $"{normalized.ContentHash}-thumb.webp";
        Directory.CreateDirectory(factory.UploadsPath);
        await File.WriteAllBytesAsync(
            Path.Combine(factory.UploadsPath, imagePath),
            normalized.Original);
        await File.WriteAllBytesAsync(
            Path.Combine(factory.UploadsPath, thumbPath),
            normalized.Thumbnail);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var garment = NewGarment(name, GarmentCategory.Top);
        garment.ContentHash = normalized.ContentHash;
        garment.ImagePath = imagePath;
        garment.ThumbPath = thumbPath;
        db.Garments.Add(garment);
        await db.SaveChangesAsync();
        return garment.Id;
    }

    public static async Task SeedGarments(TestAppFactory factory, params Garment[] garments)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Garments.AddRange(garments);
        await db.SaveChangesAsync();
    }
}

/// <summary>
/// Simulates an AI provider that is down or timing out. The upload endpoint
/// must still persist the garment with zero confidence.
/// </summary>
public sealed class ThrowingAnalyzer : IGarmentAnalyzer
{
    public Task<GarmentAnalysis> AnalyzeAsync(
        byte[] image,
        string contentType,
        CancellationToken ct) =>
        throw new HttpRequestException("El proveedor de IA no está disponible.");
}

/// <summary>
/// Simulates the model answering <c>{"error":"no_garment"}</c>. The endpoints
/// must surface a 422 instead of storing a zero-confidence "Prenda".
/// </summary>
public sealed class NoGarmentAnalyzer : IGarmentAnalyzer
{
    public Task<GarmentAnalysis> AnalyzeAsync(
        byte[] image,
        string contentType,
        CancellationToken ct) =>
        throw new NoGarmentDetectedException();
}
