using System.Text.Json;
using FashionApp.Api.Domain;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Ai;
using FashionApp.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace FashionApp.Api.Features.Garments;

public sealed record CreateGarmentResponse(GarmentDto Garment, bool Deduplicated);

public static class GarmentUploadEndpoint
{
    private const long MaxFileBytes = 10 * 1024 * 1024;

    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public static IEndpointRouteBuilder MapGarmentUploadEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/garments", HandleAsync)
            .DisableAntiforgery();

        return app;
    }

    private static async Task<IResult> HandleAsync(
        IFormFile? file,
        AppDbContext db,
        IImageNormalizer normalizer,
        IGarmentAnalyzer analyzer,
        FileStorage storage,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "Archivo inválido",
                "Selecciona una imagen para subir.");
        }

        if (file.Length > MaxFileBytes)
            return TooLarge();

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            return UnsupportedFormat();

        var input = await ReadAllBytesAsync(file, cancellationToken);
        if (input.LongLength > MaxFileBytes)
            return TooLarge();

        // The extension is only a hint: reject content that does not start with
        // a supported signature before handing it to the decoder.
        if (!HasSupportedSignature(input))
            return UnsupportedFormat();

        NormalizedImage normalized;
        try
        {
            using var imageStream = new MemoryStream(input, writable: false);
            normalized = await normalizer.NormalizeAsync(imageStream, cancellationToken);
        }
        catch (InvalidImageException)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "Imagen inválida",
                "La imagen está dañada o no se pudo leer.");
        }

        // Check for an existing hash before touching the disk or the analyzer:
        // re-uploading the same photo must not create a second file or AI call.
        var existing = await db.Garments
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.ContentHash == normalized.ContentHash, cancellationToken);

        if (existing is not null)
            return Results.Ok(new CreateGarmentResponse(GarmentQuery.ToDto(existing), true));

        GarmentAnalysis analysis;
        try
        {
            analysis = await analyzer.AnalyzeAsync(
                normalized.Original,
                "image/webp",
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // A down provider must never lose the garment: store it with zero
            // confidence so the user can retry the analysis later.
            analysis = GarmentAnalysis.Empty();
        }

        var (imagePath, thumbPath) = await storage.SaveAsync(normalized, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var garment = ToGarment(analysis, normalized, imagePath, thumbPath, now);

        db.Garments.Add(garment);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two concurrent uploads of the same photo: the unique ContentHash
            // index wins the race, so return the row that was stored first.
            db.Entry(garment).State = EntityState.Detached;
            var winner = await db.Garments
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.ContentHash == normalized.ContentHash, cancellationToken);

            if (winner is null)
                throw;

            return Results.Ok(new CreateGarmentResponse(GarmentQuery.ToDto(winner), true));
        }

        return Results.Created(
            $"/api/garments/{garment.Id}",
            new CreateGarmentResponse(GarmentQuery.ToDto(garment), false));
    }

    private static async Task<byte[]> ReadAllBytesAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static Garment ToGarment(
        GarmentAnalysis analysis,
        NormalizedImage image,
        string imagePath,
        string thumbPath,
        DateTimeOffset now)
    {
        var colors = (analysis.Colors ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .ToArray();
        var seasons = (analysis.Seasons ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToArray();
        var styleTags = (analysis.StyleTags ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .ToArray();

        var name = analysis.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            name = "Prenda";

        return new Garment
        {
            Id = Guid.NewGuid(),
            Name = name,
            Category = ParseCategory(analysis.Category),
            Subcategory = analysis.Subcategory?.Trim(),
            ColorName = colors.FirstOrDefault() ?? "",
            ColorHex = "",
            SecondaryColorsJson = colors.Length > 1
                ? JsonSerializer.Serialize(colors.Skip(1).ToArray())
                : null,
            Pattern = analysis.Pattern?.Trim(),
            Seasons = string.Join(',', seasons),
            Formality = Math.Clamp(analysis.Formality, 1, 5),
            Material = analysis.Material?.Trim(),
            StyleTagsJson = JsonSerializer.Serialize(styleTags),
            ContentHash = image.ContentHash,
            ImagePath = imagePath,
            ThumbPath = thumbPath,
            AnalysisJson = JsonSerializer.Serialize(analysis),
            Confidence = Math.Clamp(analysis.Confidence, 0, 1),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static GarmentCategory ParseCategory(string? category) =>
        Enum.TryParse<GarmentCategory>(category?.Trim(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            ? parsed
            : GarmentCategory.Top;

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

    private static IResult TooLarge() => Problem(
        StatusCodes.Status413PayloadTooLarge,
        "Archivo demasiado grande",
        "La imagen no puede superar los 10 MB.");

    private static IResult UnsupportedFormat() => Problem(
        StatusCodes.Status415UnsupportedMediaType,
        "Formato no soportado",
        "Solo se aceptan imágenes JPEG, PNG o WebP.");

    private static IResult Problem(int statusCode, string title, string detail) =>
        Results.Problem(statusCode: statusCode, title: title, detail: detail);
}
