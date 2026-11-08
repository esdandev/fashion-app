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

        // Check for an existing hash before touching the disk or the analyzer:
        // re-uploading the same photo must not create a second file or AI call.
        var existing = await db.Garments
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.ContentHash == normalized.ContentHash, cancellationToken);

        if (existing is not null)
            return Results.Ok(new CreateGarmentResponse(GarmentQuery.ToDto(existing), true));

        // Nothing is charged for a duplicate: the AI is not called for it. Every
        // real analysis consumes one unit before touching the provider.
        if (!await quota.TryConsumeAsync(AiQuotaKinds.Analysis, cancellationToken))
            return QuotaResults.TooManyRequests(httpContext);

        var analysis = await GarmentImageIntake.AnalyzeAsync(
            analyzer,
            normalized.Original,
            cancellationToken);

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
            Category = GarmentAnalysisMapping.ParseCategory(analysis.Category),
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
}
