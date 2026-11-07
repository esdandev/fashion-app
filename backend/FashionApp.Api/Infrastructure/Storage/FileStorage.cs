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

    /// <summary>
    /// Writes through a temporary file in the same directory and publishes it
    /// with an atomic rename. A crash, cancellation or I/O error can only leave
    /// the temporary file behind, never a truncated <c>{hash}.webp</c> that the
    /// immutable media cache would serve forever.
    /// </summary>
    private static async Task WriteIfMissingAsync(string path, byte[] bytes, CancellationToken ct)
    {
        if (File.Exists(path))
            return;

        var directory = Path.GetDirectoryName(path)!;
        var tempPath = Path.Combine(
            directory,
            $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);
            }

            try
            {
                // Atomic on the same volume; never overwrite content-addressed files.
                File.Move(tempPath, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another upload of the same image won the race: its complete
                // file is already in place, so this one is not needed.
                TryDelete(tempPath);
            }
        }
        catch
        {
            // Cancellation, disk-full, sharing violations, etc.: remove the
            // partial temporary file and let the caller surface the failure.
            TryDelete(tempPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort cleanup; the temporary file is never served by /media.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: cleanup is best effort and must not mask the original error.
        }
    }
}
