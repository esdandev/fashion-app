namespace FashionApp.Api.Infrastructure.Storage;

public sealed record NormalizedImage(
    byte[] Original,
    byte[] Thumbnail,
    string ContentHash,
    int Width,
    int Height);

public interface IImageNormalizer
{
    Task<NormalizedImage> NormalizeAsync(Stream input, CancellationToken ct);
}

public sealed class InvalidImageException(string message) : Exception(message);
