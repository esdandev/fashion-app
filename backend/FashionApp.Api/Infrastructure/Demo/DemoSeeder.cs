using System.Text.Json;
using FashionApp.Api.Domain;
using FashionApp.Api.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FashionApp.Api.Infrastructure.Demo;

public sealed record DemoSeedItem(
    string Name,
    string Category,
    string ColorName,
    string ColorHex,
    string[] Seasons,
    int Formality,
    string[] StyleTags,
    string File);

public sealed record DemoSeedManifest(int Version, string? Note, DemoSeedItem[] Items);

/// <summary>
/// Seeds the public demo wardrobe from <c>SeedAssets/manifest.json</c>. Images
/// are copied to the uploads root content-addressed, exactly like a real
/// upload, but without calling the AI. Seeding only happens when the Garments
/// table is empty, so restarting the container never duplicates rows.
/// </summary>
public static class DemoSeeder
{
    private const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private static string AssetsDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Infrastructure", "Demo", "SeedAssets");

    public static Task SeedAsync(AppDbContext db) =>
        SeedAsync(db, uploadsRoot: null, CancellationToken.None);

    public static async Task SeedAsync(
        AppDbContext db,
        string? uploadsRoot,
        CancellationToken ct = default)
    {
        if (await db.Garments.AnyAsync(ct))
            return;

        var items = await ReadManifestAsync(ct);
        var root = string.IsNullOrWhiteSpace(uploadsRoot)
            ? ResolveUploadsRoot(db)
            : uploadsRoot;

        Directory.CreateDirectory(root);

        var normalizer = new MagickImageNormalizer();
        var now = DateTimeOffset.UtcNow;

        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            var source = await ReadAssetAsync(item, ct);

            NormalizedImage normalized;
            using (var stream = new MemoryStream(source, writable: false))
            {
                normalized = await normalizer.NormalizeAsync(stream, ct);
            }

            var imagePath = $"{normalized.ContentHash}.webp";
            var thumbPath = $"{normalized.ContentHash}-thumb.webp";

            await FileStorage.WriteIfMissingAsync(
                Path.Combine(root, imagePath), normalized.Original, ct);
            await FileStorage.WriteIfMissingAsync(
                Path.Combine(root, thumbPath), normalized.Thumbnail, ct);

            db.Garments.Add(new Garment
            {
                Id = Guid.NewGuid(),
                Name = item.Name,
                Category = ParseCategory(item.Category),
                ColorName = item.ColorName,
                ColorHex = item.ColorHex,
                Seasons = string.Join(',', item.Seasons),
                Formality = Math.Clamp(item.Formality, 1, 5),
                StyleTagsJson = JsonSerializer.Serialize(item.StyleTags),
                ContentHash = normalized.ContentHash,
                ImagePath = imagePath,
                ThumbPath = thumbPath,
                Confidence = 1,
                IsArchived = false,
                // Keep the manifest order stable in the CreatedAt DESC listing.
                CreatedAt = now.AddSeconds(-index),
                UpdatedAt = now.AddSeconds(-index)
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<DemoSeedItem[]> ReadManifestAsync(CancellationToken ct)
    {
        var path = Path.Combine(AssetsDirectory, ManifestFileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"No se encontró el manifiesto de la demo en '{path}'.");
        }

        await using var stream = File.OpenRead(path);
        var manifest = await JsonSerializer.DeserializeAsync<DemoSeedManifest>(
            stream, JsonOptions, ct);

        return manifest?.Items is { Length: > 0 } items
            ? items
            : throw new InvalidOperationException("El manifiesto de la demo no contiene prendas.");
    }

    private static async Task<byte[]> ReadAssetAsync(DemoSeedItem item, CancellationToken ct)
    {
        var path = Path.Combine(AssetsDirectory, item.File);
        if (File.Exists(path))
            return await File.ReadAllBytesAsync(path, ct);

        // The committed assets are generated deterministically; regenerating a
        // missing one keeps the demo self-healing without shipping stock photos.
        return DemoAssetFactory.CreateWebp(item);
    }

    private static string ResolveUploadsRoot(AppDbContext db)
    {
        var connectionString = db.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            return Path.Combine(Directory.GetCurrentDirectory(), "uploads");

        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:")
            return Path.Combine(Directory.GetCurrentDirectory(), "uploads");

        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource))
            ?? Directory.GetCurrentDirectory();

        return Path.Combine(directory, "uploads");
    }

    private static GarmentCategory ParseCategory(string category) =>
        Enum.TryParse<GarmentCategory>(category, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            ? parsed
            : GarmentCategory.Top;
}
