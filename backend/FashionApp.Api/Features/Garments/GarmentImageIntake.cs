using FashionApp.Api.Infrastructure.Ai;
using FashionApp.Api.Infrastructure.Storage;

namespace FashionApp.Api.Features.Garments;

/// <summary>
/// Shared multipart intake for the upload and analyze endpoints. Both accept the
/// same formats, enforce the same 10 MB limit and normalize the image before any
/// other work happens, so a corrupt file never reaches the database or the AI.
/// </summary>
internal static class GarmentImageIntake
{
    public const long MaxFileBytes = 10 * 1024 * 1024;

    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public static async Task<(NormalizedImage? Image, IResult? Error)> TryNormalizeAsync(
        IFormFile? file,
        IImageNormalizer normalizer,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return (null, InvalidFile());

        if (file.Length > MaxFileBytes)
            return (null, TooLarge());

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            return (null, UnsupportedFormat());

        var input = await ReadAllBytesAsync(file, cancellationToken);
        if (input.LongLength > MaxFileBytes)
            return (null, TooLarge());

        // The extension is only a hint: reject content that does not start with
        // a supported signature before handing it to the decoder.
        if (!HasSupportedSignature(input))
            return (null, UnsupportedFormat());

        try
        {
            using var imageStream = new MemoryStream(input, writable: false);
            var normalized = await normalizer.NormalizeAsync(imageStream, cancellationToken);
            return (normalized, null);
        }
        catch (InvalidImageException)
        {
            return (null, Problem(
                StatusCodes.Status400BadRequest,
                "Imagen inválida",
                "La imagen está dañada o no se pudo leer."));
        }
    }

    /// <summary>
    /// Runs the analyzer with the product contract: a down provider never turns
    /// into a 500, it returns <see cref="GarmentAnalysis.Empty"/> so the photo can
    /// still be confirmed manually (and retried later).
    /// </summary>
    public static async Task<GarmentAnalysis> AnalyzeAsync(
        IGarmentAnalyzer analyzer,
        byte[] image,
        CancellationToken cancellationToken)
    {
        try
        {
            return await analyzer.AnalyzeAsync(image, "image/webp", cancellationToken);
        }
        catch (NoGarmentDetectedException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return GarmentAnalysis.Empty();
        }
    }

    public static IResult InvalidFile() => Problem(
        StatusCodes.Status400BadRequest,
        "Archivo inválido",
        "Selecciona una imagen para subir.");

    public static IResult TooLarge() => Problem(
        StatusCodes.Status413PayloadTooLarge,
        "Archivo demasiado grande",
        "La imagen no puede superar los 10 MB.");

    public static IResult UnsupportedFormat() => Problem(
        StatusCodes.Status415UnsupportedMediaType,
        "Formato no soportado",
        "Solo se aceptan imágenes JPEG, PNG o WebP.");

    public static IResult NoGarmentDetected() => Problem(
        StatusCodes.Status422UnprocessableEntity,
        "No se detectó una prenda",
        "La foto no parece contener una prenda. Probá con otra imagen donde se vea la prenda completa.");

    public static IResult Problem(int statusCode, string title, string detail) =>
        Results.Problem(statusCode: statusCode, title: title, detail: detail);

    private static async Task<byte[]> ReadAllBytesAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static bool HasSupportedSignature(byte[] content)
    {
        // JPEG: FF D8 FF
        if (content.Length >= 3
            && content[0] == 0xFF
            && content[1] == 0xD8
            && content[2] == 0xFF)
        {
            return true;
        }

        // PNG: 89 50 4E 47
        if (content.Length >= 4
            && content[0] == 0x89
            && content[1] == 0x50
            && content[2] == 0x4E
            && content[3] == 0x47)
        {
            return true;
        }

        // WebP: "RIFF" .... "WEBP"
        if (content.Length >= 12
            && content[0] == (byte)'R'
            && content[1] == (byte)'I'
            && content[2] == (byte)'F'
            && content[3] == (byte)'F'
            && content[8] == (byte)'W'
            && content[9] == (byte)'E'
            && content[10] == (byte)'B'
            && content[11] == (byte)'P')
        {
            return true;
        }

        return false;
    }
}
