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
        DataPaths paths,
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
            .FirstOrDefaultAsync(
                g => g.ContentHash == hash && !g.IsArchived,
                cancellationToken);

        if (existing is not null)
            return Results.Ok(new CreateGarmentResponse(GarmentQuery.ToDto(existing), true));

        var validationError = GarmentInputRules.Validate(
            request.Name,
            requireName: true,
            request.Category,
            request.Subcategory,
            request.ColorName,
            request.ColorHex,
            request.Pattern,
            request.Material,
            request.Notes,
            request.Formality,
            request.Seasons,
            request.StyleTags);

        if (validationError is not null)
        {
            return GarmentImageIntake.Problem(
                StatusCodes.Status400BadRequest,
                "Solicitud inválida",
                validationError);
        }

        var category = GarmentAnalysisMapping.ParseCategory(request.Category);
        var imageFile = $"{hash}.webp";
        var thumbFile = $"{hash}-thumb.webp";

        if (!File.Exists(Path.Combine(paths.UploadsPath, imageFile))
            || !File.Exists(Path.Combine(paths.UploadsPath, thumbFile)))
        {
            return GarmentImageIntake.Problem(
                StatusCodes.Status409Conflict,
                "Imagen no disponible",
                "La imagen analizada ya no está disponible. Volvé a subirla.");
        }

        var name = request.Name?.Trim() ?? "";
        var seasons = GarmentInputRules.NormalizeSeasons(request.Seasons);
        var styleTags = GarmentInputRules.NormalizeStyleTags(request.StyleTags);
        var colorName = request.ColorName?.Trim() ?? "";
        var colorHex = string.IsNullOrWhiteSpace(request.ColorHex)
            ? ""
            : request.ColorHex.Trim();
        var confidence = request.Confidence is double value && double.IsFinite(value)
            ? Math.Clamp(value, 0, 1)
            : 0;
        var finalFormality = request.Formality ?? 3;
        var now = DateTimeOffset.UtcNow;

        // The same photo may belong to an archived garment. Restoring that row
        // (instead of inserting a duplicate hash) keeps the wardrobe consistent.
        var archived = await db.Garments
            .FirstOrDefaultAsync(
                g => g.ContentHash == hash && g.IsArchived,
                cancellationToken);

        if (archived is not null)
        {
            ApplyConfirmedValues(
                archived,
                name,
                category,
                request,
                colorName,
                colorHex,
                seasons,
                styleTags,
                finalFormality,
                confidence);
            archived.IsArchived = false;
            archived.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new CreateGarmentResponse(GarmentQuery.ToDto(archived), true));
        }

        var garment = new Garment
        {
            Id = Guid.NewGuid(),
            ContentHash = hash,
            ImagePath = imageFile,
            ThumbPath = thumbFile,
            CreatedAt = now,
            UpdatedAt = now
        };
        ApplyConfirmedValues(
            garment,
            name,
            category,
            request,
            colorName,
            colorHex,
            seasons,
            styleTags,
            finalFormality,
            confidence);

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
                .FirstOrDefaultAsync(
                    g => g.ContentHash == hash && !g.IsArchived,
                    cancellationToken);

            if (winner is null)
                throw;

            return Results.Ok(new CreateGarmentResponse(GarmentQuery.ToDto(winner), true));
        }

        return Results.Created(
            $"/api/garments/{garment.Id}",
            new CreateGarmentResponse(GarmentQuery.ToDto(garment), false));
    }

    private static void ApplyConfirmedValues(
        Garment garment,
        string name,
        GarmentCategory category,
        ConfirmGarmentRequest request,
        string colorName,
        string colorHex,
        string[] seasons,
        string[] styleTags,
        int formality,
        double confidence)
    {
        var analysis = new GarmentAnalysis(
            Name: name,
            Category: GarmentAnalysisMapping.CategoryToApi(category),
            Subcategory: request.Subcategory?.Trim(),
            Colors: colorName.Length == 0 ? [] : [colorName],
            Pattern: request.Pattern?.Trim(),
            Seasons: seasons,
            Formality: formality,
            Material: request.Material?.Trim(),
            StyleTags: styleTags,
            Confidence: confidence);

        garment.Name = name;
        garment.Category = category;
        garment.Subcategory = request.Subcategory?.Trim();
        garment.ColorName = colorName;
        garment.ColorHex = colorHex;
        garment.Pattern = request.Pattern?.Trim();
        garment.Seasons = string.Join(',', seasons);
        garment.Formality = formality;
        garment.Material = request.Material?.Trim();
        garment.StyleTagsJson = JsonSerializer.Serialize(styleTags);
        garment.Notes = request.Notes?.Trim();
        garment.AnalysisJson = JsonSerializer.Serialize(analysis);
        garment.Confidence = confidence;
    }

    [GeneratedRegex("^[a-f0-9]{64}$")]
    private static partial Regex ContentHashPattern();
}
