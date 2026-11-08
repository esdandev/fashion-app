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

        // Archived rows must not short-circuit the flow: the user is uploading
        // the photo again to bring the garment back.
        var existing = await db.Garments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                g => g.ContentHash == normalized.ContentHash && !g.IsArchived,
                cancellationToken);

        if (existing is not null)
        {
            // Re-analyzing an already saved photo costs no quota: return its id,
            // the stored analysis and the current confirmed values, so the form
            // opens with what is actually in the wardrobe.
            return Results.Ok(new AnalyzeGarmentResponse(
                normalized.ContentHash,
                MediaUrl(normalized.ContentHash),
                ThumbUrl(normalized.ContentHash),
                GarmentAnalysisMapping.FromGarment(existing),
                existing.Id,
                GarmentAnalysisMapping.ToCurrentGarment(existing)));
        }

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

        GarmentAnalysisValidator.TryNormalize(analysis, out var proposal);

        await storage.SaveAsync(normalized, cancellationToken);

        return Results.Ok(new AnalyzeGarmentResponse(
            normalized.ContentHash,
            MediaUrl(normalized.ContentHash),
            ThumbUrl(normalized.ContentHash),
            proposal,
            null,
            null));
    }

    private static string MediaUrl(string contentHash) => $"/media/{contentHash}.webp";

    private static string ThumbUrl(string contentHash) => $"/media/{contentHash}-thumb.webp";
}
