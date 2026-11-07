using System.Security.Cryptography;
using ImageMagick;

namespace FashionApp.Api.Infrastructure.Storage;

public sealed class MagickImageNormalizer : IImageNormalizer
{
    private const int MaxDimension = 1600;
    private const int ThumbnailDimension = 400;
    private const int OriginalQuality = 82;
    private const int ThumbnailQuality = 75;

    public async Task<NormalizedImage> NormalizeAsync(Stream input, CancellationToken ct)
    {
        var image = new MagickImage();
        try
        {
            await image.ReadAsync(input, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            image.Dispose();
            throw new InvalidImageException("La imagen no se pudo leer.");
        }

        using (image)
        {
            image.AutoOrient();
            image.Strip();

            if (Math.Max(image.Width, image.Height) > MaxDimension)
            {
                image.Resize(new MagickGeometry(MaxDimension, MaxDimension) { Greater = true });
            }

            var originalBytes = await EncodeWebpAsync(image, OriginalQuality, ct);
            byte[] thumbnailBytes;

            if (Math.Max(image.Width, image.Height) > ThumbnailDimension)
            {
                using var thumbnail = (MagickImage)image.Clone();
                thumbnail.Resize(
                    new MagickGeometry(ThumbnailDimension, ThumbnailDimension) { Greater = true });
                thumbnailBytes = await EncodeWebpAsync(thumbnail, ThumbnailQuality, ct);
            }
            else
            {
                thumbnailBytes = await EncodeWebpAsync(image, ThumbnailQuality, ct);
            }

            var contentHash = Convert
                .ToHexString(SHA256.HashData(originalBytes))
                .ToLowerInvariant();

            return new NormalizedImage(
                originalBytes,
                thumbnailBytes,
                contentHash,
                (int)image.Width,
                (int)image.Height);
        }
    }

    private static async Task<byte[]> EncodeWebpAsync(
        MagickImage image,
        int quality,
        CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        image.Quality = (uint)quality;
        await image.WriteAsync(buffer, MagickFormat.WebP, ct);
        return buffer.ToArray();
    }
}
