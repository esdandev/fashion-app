using FashionApp.Api.Features.Security;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Ai;
using FashionApp.Api.Infrastructure.Quotas;
using FashionApp.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace FashionApp.Api.Features.Garments;

/// <summary>
/// Analyzes a photo without creating a <c>Garment</c>. The normalized image is
/// content-addressed and stays on disk, so a later confirm can create the row
/// (or reuse it after a cancelled dialog) without another AI call.
/// </summary>
public static class GarmentAnalyzeEndpoint
{
    public static IEndpointRouteBuilder MapGarmentAnalyzeEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/garments/analyze", HandleAsync)
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

        var existing = await db.Garments
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.ContentHash == normalized.ContentHash, cancellationToken);

        if (existing is not null)
        {
            // Re-analyzing an already saved photo costs no quota: return its id
            // and the analysis we stored, so the UI can offer reanalysis/edit.
            return Results.Ok(new AnalyzeGarmentResponse(
                normalized.ContentHash,
                MediaUrl(normalized.ContentHash),
                ThumbUrl(normalized.ContentHash),
                GarmentAnalysisMapping.FromGarment(existing),
                existing.Id));
        }

        if (!await quota.TryConsumeAsync(AiQuotaKinds.Analysis, cancellationToken))
            return QuotaResults.TooManyRequests(httpContext);

        var analysis = await GarmentImageIntake.AnalyzeAsync(
            analyzer,
            normalized.Original,
            cancellationToken);

        GarmentAnalysisValidator.TryNormalize(analysis, out var proposal);

        await storage.SaveAsync(normalized, cancellationToken);

        return Results.Ok(new AnalyzeGarmentResponse(
            normalized.ContentHash,
            MediaUrl(normalized.ContentHash),
            ThumbUrl(normalized.ContentHash),
            proposal,
            null));
    }

    private static string MediaUrl(string contentHash) => $"/media/{contentHash}.webp";

    private static string ThumbUrl(string contentHash) => $"/media/{contentHash}-thumb.webp";
}
