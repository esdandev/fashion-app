using System.Text.Json;
using FashionApp.Api.Domain;
using FashionApp.Api.Infrastructure.Storage;
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

    public static async Task SeedAsync(
        AppDbContext db,
        string uploadsRoot,
        CancellationToken ct = default)
    {
        if (await db.Garments.AnyAsync(ct))
            return;

        var items = await ReadManifestAsync(ct);
        var root = uploadsRoot;

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

    /// <summary>
    /// Refuses to start a demo that points at a database with rows outside the
    /// demo manifest. This is the guard that keeps a misconfigured
    /// <c>Demo:DataRoot</c> from exposing personal garments or erasing them on a
    /// demo reset.
    /// </summary>
    public static async Task EnsureOnlyDemoDataAsync(
        AppDbContext db,
        CancellationToken ct = default)
    {
        var stored = await db.Garments
            .AsNoTracking()
            .Select(g => new { g.ContentHash, g.Name })
            .ToListAsync(ct);

        if (stored.Count == 0)
            return;

        var allowed = await ComputeManifestHashesAsync(ct);
        var foreign = stored.FirstOrDefault(g => !allowed.Contains(g.ContentHash));

        if (foreign is not null)
        {
            throw new InvalidOperationException(
                $"La demo no puede usar esta base de datos: contiene la prenda '{foreign.Name}', " +
                "que no pertenece al set de demo. Configurá Demo:DataRoot en un directorio propio " +
                "(por defecto data/demo) y no borres la carpeta data/ personal.");
        }
    }

    /// <summary>Content hashes of the images described by the demo manifest.</summary>
    public static async Task<HashSet<string>> ComputeManifestHashesAsync(
        CancellationToken ct = default)
    {
        var items = await ReadManifestAsync(ct);
        var normalizer = new MagickImageNormalizer();
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var source = await ReadAssetAsync(item, ct);
            using var stream = new MemoryStream(source, writable: false);
            var normalized = await normalizer.NormalizeAsync(stream, ct);
            hashes.Add(normalized.ContentHash);
        }

        return hashes;
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

    private static GarmentCategory ParseCategory(string category) =>
        Enum.TryParse<GarmentCategory>(category, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            ? parsed
            : GarmentCategory.Top;
}
