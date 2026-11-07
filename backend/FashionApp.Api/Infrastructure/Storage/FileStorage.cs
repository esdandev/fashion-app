namespace FashionApp.Api.Infrastructure.Storage;

/// <summary>
/// Persists normalized images under the configured storage root. Files are
/// content addressed, so an existing file is always kept as-is.
/// </summary>
public sealed class FileStorage(IConfiguration configuration)
{
    public string Root => configuration["Storage:Root"] ?? "data/uploads";

    public async Task<(string ImagePath, string ThumbPath)> SaveAsync(
        NormalizedImage image,
        CancellationToken ct)
    {
        Directory.CreateDirectory(Root);

        var imagePath = $"{image.ContentHash}.webp";
        var thumbPath = $"{image.ContentHash}-thumb.webp";

        await WriteIfMissingAsync(Path.Combine(Root, imagePath), image.Original, ct);
        await WriteIfMissingAsync(Path.Combine(Root, thumbPath), image.Thumbnail, ct);

        return (imagePath, thumbPath);
    }

    private static async Task WriteIfMissingAsync(string path, byte[] bytes, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            await stream.WriteAsync(bytes, ct);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Content-addressed file already on disk: the existing copy wins.
        }
    }
}
