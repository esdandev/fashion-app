using System.Text.Json;
using FashionApp.Api.Domain;
using FashionApp.Api.Features.Security;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Ai;
using FashionApp.Api.Infrastructure.Quotas;
using FashionApp.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace FashionApp.Api.Features.Garments;

public sealed record CreateGarmentResponse(GarmentDto Garment, bool Deduplicated);

public static class GarmentUploadEndpoint
{
    public static IEndpointRouteBuilder MapGarmentUploadEndpoint(this IEndpointRouteBuilder app)
    {
        // Kept for compatibility: the frontend uses analyze + confirm so a
        // cancelled dialog never creates a database row.
        app.MapPost("/api/garments", HandleAsync)
            .DisableAntiforgery()
            .BlockWritesInDemo()
            .RequireToken();

        return app;
    }

    private static async Task<IResult> HandleAsync(
        IFormFile? file,
        AppDbContext db,
        IImageNormalizer normalizer,
        IGarmentAnalyzer analyzer,
        IAiQuota quota,
        FileStorage storage,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var (normalized, error) = await GarmentImageIntake.TryNormalizeAsync(
            file,
            normalizer,
            cancellationToken);

        if (normalized is null)
            return error ?? GarmentImageIntake.InvalidFile();

        // Check for an active existing hash before touching the disk or the
        // analyzer: re-uploading the same photo must not create a second file or
        // AI call. Archived rows are ignored so the user can restore them.
        var existing = await db.Garments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                g => g.ContentHash == normalized.ContentHash && !g.IsArchived,
                cancellationToken);

        if (existing is not null)
            return Results.Ok(new CreateGarmentResponse(GarmentQuery.ToDto(existing), true));

        // Nothing is charged for a duplicate: the AI is not called for it. Every
        // real analysis consumes one unit before touching the provider.
        if (!await quota.TryConsumeAsync(AiQuotaKinds.Analysis, cancellationToken))
            return QuotaResults.TooManyRequests(httpContext, AiQuotaKinds.Analysis);

        GarmentAnalysis analysis;
        try
        {
            analysis = await GarmentImageIntake.AnalyzeAsync(
                analyzer,
                normalized.Original,
                cancellationToken);
        }
        catch (NoGarmentDetectedException)
        {
            return GarmentImageIntake.NoGarmentDetected();
        }

        var (imagePath, thumbPath) = await storage.SaveAsync(normalized, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        // An archived row still owns the unique ContentHash. Restoring it keeps
        // the original id and avoids a unique-index failure on re-upload.
        var archived = await db.Garments
            .FirstOrDefaultAsync(
                g => g.ContentHash == normalized.ContentHash && g.IsArchived,
                cancellationToken);

        if (archived is not null)
        {
            ApplyValues(archived, analysis, imagePath, thumbPath);
            archived.IsArchived = false;
            archived.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new CreateGarmentResponse(GarmentQuery.ToDto(archived), true));
        }

        var garment = new Garment
        {
            Id = Guid.NewGuid(),
            ContentHash = normalized.ContentHash,
            ImagePath = imagePath,
            ThumbPath = thumbPath,
            CreatedAt = now,
            UpdatedAt = now
        };
        ApplyValues(garment, analysis, imagePath, thumbPath);

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
    }

    private static void ApplyValues(
        Garment garment,
        GarmentAnalysis analysis,
        string imagePath,
        string thumbPath)
    {
        var colors = (analysis.Colors ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .ToArray();
        var seasons = GarmentInputRules.NormalizeSeasons(analysis.Seasons);
        var styleTags = GarmentInputRules.NormalizeStyleTags(analysis.StyleTags);

        var name = analysis.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            name = "Prenda";

        garment.Name = name;
        garment.Category = GarmentAnalysisMapping.ParseCategory(analysis.Category);
        garment.Subcategory = analysis.Subcategory?.Trim();
        garment.ColorName = colors.FirstOrDefault() ?? "";
        garment.ColorHex = "";
        garment.SecondaryColorsJson = colors.Length > 1
            ? JsonSerializer.Serialize(colors.Skip(1).ToArray())
            : null;
        garment.Pattern = analysis.Pattern?.Trim();
        garment.Seasons = string.Join(',', seasons);
        garment.Formality = Math.Clamp(analysis.Formality, 1, 5);
        garment.Material = analysis.Material?.Trim();
        garment.StyleTagsJson = JsonSerializer.Serialize(styleTags);
        garment.ImagePath = imagePath;
        garment.ThumbPath = thumbPath;
        garment.AnalysisJson = JsonSerializer.Serialize(analysis);
        garment.Confidence = Math.Clamp(analysis.Confidence, 0, 1);
    }
}
