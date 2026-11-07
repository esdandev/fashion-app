using FashionApp.Api.Domain;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
