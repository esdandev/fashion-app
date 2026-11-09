using FashionApp.Api.Domain;
using FashionApp.Api.Features.Security;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.RateLimiting;
using FashionApp.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace FashionApp.Api.Features.Garments;

public sealed record CreateGarmentResponse(GarmentDto Garment, bool Deduplicated);

/// <summary>
/// Manual garment upload. The image is normalized and stored, and every
/// attribute comes from the form: there is no AI analysis in this flow.
/// </summary>
public static class GarmentUploadEndpoint
{
    public static IEndpointRouteBuilder MapGarmentUploadEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/garments", HandleAsync)
            .DisableAntiforgery()
            .BlockWritesInDemo()
            .RequireToken()
            .RequireRateLimiting(ClientRateLimiting.Policy);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest request,
        AppDbContext db,
        IImageNormalizer normalizer,
        FileStorage storage,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");

        var (normalized, error) = await GarmentImageIntake.TryNormalizeAsync(
            file,
            normalizer,
            cancellationToken);

        if (normalized is null)
            return error ?? GarmentImageIntake.InvalidFile();

        var name = form["name"].ToString().Trim();
        var category = form["category"].ToString().Trim();
        var subcategory = NullIfBlank(form["subcategory"]);
        var colorName = form["colorName"].ToString().Trim();
        var colorHex = form["colorHex"].ToString().Trim();
        var pattern = NullIfBlank(form["pattern"]);
        var material = NullIfBlank(form["material"]);
        var brand = NullIfBlank(form["brand"]);
        var notes = NullIfBlank(form["notes"]);
        var formality = int.TryParse(form["formality"], out var parsedFormality)
            ? parsedFormality
            : 3;
        var seasons = SplitList(form["seasons"]);
        var styleTags = SplitList(form["styleTags"]);

        var validationError = GarmentInputRules.Validate(
            name,
            requireName: true,
            category,
            subcategory,
            colorName,
            colorHex,
            pattern,
            material,
            notes,
            formality,
            seasons,
            styleTags);

        if (validationError is not null)
            return GarmentImageIntake.Problem(
                StatusCodes.Status400BadRequest,
                "Datos inválidos",
                validationError);

        if (!GarmentVocabulary.Categories.Contains(category))
            return GarmentImageIntake.Problem(
                StatusCodes.Status400BadRequest,
                "Categoría inválida",
                "Elegí una categoría válida para la prenda.");

        var normalizedSeasons = GarmentInputRules.NormalizeSeasons(seasons);
        var normalizedTags = GarmentInputRules.NormalizeStyleTags(styleTags);

        var existing = await db.Garments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                g => g.ContentHash == normalized.ContentHash && !g.IsArchived,
                cancellationToken);

        if (existing is not null)
        {
            return GarmentImageIntake.Problem(
                StatusCodes.Status409Conflict,
                "Prenda duplicada",
                "Ya existe una prenda con esa foto en el guardarropa.");
        }

        var (imagePath, thumbPath) = await storage.SaveAsync(normalized, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var archived = await db.Garments
            .FirstOrDefaultAsync(
                g => g.ContentHash == normalized.ContentHash && g.IsArchived,
                cancellationToken);

        if (archived is not null)
        {
            ApplyValues(archived, imagePath, thumbPath);
            archived.IsArchived = false;
            archived.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new CreateGarmentResponse(GarmentQuery.ToDto(archived), true));
        }

        var garment = new Garment
        {
            Id = Guid.NewGuid(),
            ContentHash = normalized.ContentHash,
            CreatedAt = now
        };
        ApplyValues(garment, imagePath, thumbPath);

        db.Garments.Add(garment);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.Entry(garment).State = EntityState.Detached;
            var winner = await db.Garments
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    g => g.ContentHash == normalized.ContentHash && !g.IsArchived,
                    cancellationToken);

            if (winner is null)
                throw;

            return Results.Ok(new CreateGarmentResponse(GarmentQuery.ToDto(winner), true));
        }

        return Results.Created(
            $"/api/garments/{garment.Id}",
            new CreateGarmentResponse(GarmentQuery.ToDto(garment), false));

        void ApplyValues(Garment target, string image, string thumb)
        {
            target.Name = name;
            target.Category = GarmentInputRules.ParseCategory(category);
            target.Subcategory = subcategory;
            target.ColorName = colorName;
            target.ColorHex = colorHex;
            target.SecondaryColorsJson = null;
            target.Pattern = pattern;
            target.Seasons = string.Join(',', normalizedSeasons);
            target.Formality = formality;
            target.Material = material;
            target.Brand = brand;
            target.StyleTagsJson = System.Text.Json.JsonSerializer.Serialize(normalizedTags);
            target.Notes = notes;
            target.ImagePath = image;
            target.ThumbPath = thumb;
            target.AnalysisJson = null;
            target.Confidence = 1;
            target.UpdatedAt = now;
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string[] SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
