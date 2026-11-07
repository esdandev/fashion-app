using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace FashionApp.Api.Infrastructure.Storage;

public sealed class ImageSharpNormalizer : IImageNormalizer
{
    private const int MaxDimension = 1600;
    private const int ThumbnailDimension = 400;
    private const int OriginalQuality = 82;
    private const int ThumbnailQuality = 75;

    public async Task<NormalizedImage> NormalizeAsync(Stream input, CancellationToken ct)
    {
        Image image;
        try
        {
            image = await Image.LoadAsync(input, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidImageException("La imagen no se pudo leer.");
        }

        using (image)
        {
            image.Mutate(x => x.AutoOrient());

            if (Math.Max(image.Width, image.Height) > MaxDimension)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(MaxDimension, MaxDimension),
                    Mode = ResizeMode.Max
                }));
            }

            var originalBytes = await EncodeWebpAsync(image, OriginalQuality, ct);
            byte[] thumbnailBytes;

            if (Math.Max(image.Width, image.Height) > ThumbnailDimension)
            {
                using var thumbnail = image.Clone(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(ThumbnailDimension, ThumbnailDimension),
                    Mode = ResizeMode.Max
                }));
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
                image.Width,
                image.Height);
        }
    }

    private static async Task<byte[]> EncodeWebpAsync(
        Image image,
        int quality,
        CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await image.SaveAsWebpAsync(
            buffer,
            new WebpEncoder { Quality = quality, SkipMetadata = true },
            ct);
        return buffer.ToArray();
    }
}
