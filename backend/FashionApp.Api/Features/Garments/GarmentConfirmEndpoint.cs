using System.Text.Json;
using System.Text.RegularExpressions;
using FashionApp.Api.Domain;
using FashionApp.Api.Features.Security;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Ai;
using Microsoft.EntityFrameworkCore;

namespace FashionApp.Api.Features.Garments;

/// <summary>
/// Creates a <c>Garment</c> from the values the user confirmed after an analyze
/// call. The image must already be in content-addressed storage. No AI call and
/// no quota consumption happen here.
/// </summary>
public static partial class GarmentConfirmEndpoint
{
    public static IEndpointRouteBuilder MapGarmentConfirmEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/garments/confirm", HandleAsync)
            .BlockWritesInDemo()
            .RequireToken();

        return app;
    }

    private static async Task<IResult> HandleAsync(
        ConfirmGarmentRequest request,
        AppDbContext db,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var hash = request.ContentHash?.Trim().ToLowerInvariant() ?? "";
        if (!ContentHashPattern().IsMatch(hash))
        {
            return GarmentImageIntake.Problem(
                StatusCodes.Status400BadRequest,
                "Solicitud inválida",
                "El hash de contenido no es válido.");
        }

        var existing = await db.Garments
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.ContentHash == hash, cancellationToken);

        if (existing is not null)
            return Results.Ok(new CreateGarmentResponse(GarmentQuery.ToDto(existing), true));

        var name = request.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > 120)
        {
            return GarmentImageIntake.Problem(
                StatusCodes.Status400BadRequest,
                "Solicitud inválida",
                "El nombre no puede estar vacío ni superar los 120 caracteres.");
        }

        var category = GarmentCategory.Top;
        if (request.Category is not null
            && (!Enum.TryParse(request.Category, ignoreCase: true, out category)
                || !Enum.IsDefined(category)))
        {
            return GarmentImageIntake.Problem(
                StatusCodes.Status400BadRequest,
                "Solicitud inválida",
                "La categoría no es válida.");
        }

        if (request.Formality is int formality && (formality < 1 || formality > 5))
        {
            return GarmentImageIntake.Problem(
                StatusCodes.Status400BadRequest,
                "Solicitud inválida",
                "La formalidad debe estar entre 1 y 5.");
        }

        var root = configuration["Storage:Root"] ?? "data/uploads";
        var imageFile = $"{hash}.webp";
        var thumbFile = $"{hash}-thumb.webp";

        if (!File.Exists(Path.Combine(root, imageFile))
            || !File.Exists(Path.Combine(root, thumbFile)))
        {
            return GarmentImageIntake.Problem(
                StatusCodes.Status409Conflict,
                "Imagen no disponible",
                "La imagen analizada ya no está disponible. Volvé a subirla.");
        }

        var seasons = NormalizeList(request.Seasons);
        var styleTags = NormalizeList(request.StyleTags);
        var colorName = request.ColorName?.Trim() ?? "";
        var colorHex = request.ColorHex?.Trim() ?? "";
        var confidence = request.Confidence is double value && double.IsFinite(value)
            ? Math.Clamp(value, 0, 1)
            : 0;
        var finalFormality = request.Formality ?? 3;

        var analysis = new GarmentAnalysis(
            Name: name,
            Category: GarmentAnalysisMapping.CategoryToApi(category),
            Subcategory: request.Subcategory?.Trim(),
            Colors: colorName.Length == 0 ? [] : [colorName],
            Pattern: request.Pattern?.Trim(),
            Seasons: seasons,
            Formality: finalFormality,
            Material: request.Material?.Trim(),
            StyleTags: styleTags,
            Confidence: confidence);

        var now = DateTimeOffset.UtcNow;
        var garment = new Garment
        {
            Id = Guid.NewGuid(),
            Name = name,
            Category = category,
            Subcategory = request.Subcategory?.Trim(),
            ColorName = colorName,
            ColorHex = colorHex,
            Pattern = request.Pattern?.Trim(),
            Seasons = string.Join(',', seasons),
            Formality = finalFormality,
            Material = request.Material?.Trim(),
            StyleTagsJson = JsonSerializer.Serialize(styleTags),
            Notes = request.Notes?.Trim(),
            ContentHash = hash,
            ImagePath = imageFile,
            ThumbPath = thumbFile,
            AnalysisJson = JsonSerializer.Serialize(analysis),
            Confidence = confidence,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Garments.Add(garment);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Concurrent confirms of the same hash: the unique index wins, so
            // return the row that was stored first instead of failing.
            db.Entry(garment).State = EntityState.Detached;
            var winner = await db.Garments
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.ContentHash == hash, cancellationToken);

            if (winner is null)
                throw;

            return Results.Ok(new CreateGarmentResponse(GarmentQuery.ToDto(winner), true));
        }

        return Results.Created(
            $"/api/garments/{garment.Id}",
            new CreateGarmentResponse(GarmentQuery.ToDto(garment), false));
    }

    private static string[] NormalizeList(IEnumerable<string>? values) =>
        (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray();

    [GeneratedRegex("^[a-f0-9]{64}$")]
    private static partial Regex ContentHashPattern();
}
