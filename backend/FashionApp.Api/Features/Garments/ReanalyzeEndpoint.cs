using FashionApp.Api.Features.Security;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Ai;
using FashionApp.Api.Infrastructure.Quotas;
using Microsoft.EntityFrameworkCore;

namespace FashionApp.Api.Features.Garments;

/// <summary>
/// Re-runs the AI analysis for an existing garment and returns the result as a
/// proposal. Nothing is persisted: the user confirms the suggestion through
/// <c>PATCH /api/garments/{id}</c>, so a bad model answer never overwrites a
/// garment the user already curated.
/// </summary>
public static class ReanalyzeEndpoint
{
    public static IEndpointRouteBuilder MapGarmentReanalyzeEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/garments/{id:guid}/reanalyze", HandleAsync)
            .RequireToken()
            .BlockWritesInDemo();

        return app;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        AppDbContext db,
        IGarmentAnalyzer analyzer,
        IAiQuota quota,
        IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var garment = await db.Garments
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id && !g.IsArchived, cancellationToken);

        if (garment is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Prenda no encontrada",
                detail: "No existe una prenda activa con ese identificador.");
        }

        var root = configuration["Storage:Root"] ?? "data/uploads";
        var imagePath = Path.Combine(root, Path.GetFileName(garment.ImagePath));

        if (string.IsNullOrWhiteSpace(garment.ImagePath) || !File.Exists(imagePath))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Imagen no disponible",
                detail: "La imagen de la prenda no se encuentra en el almacenamiento.");
        }

        var image = await File.ReadAllBytesAsync(imagePath, cancellationToken);

        if (!await quota.TryConsumeAsync(AiQuotaKinds.Analysis, cancellationToken))
            return QuotaResults.TooManyRequests(httpContext);

        GarmentAnalysis analysis;
        try
        {
            analysis = await analyzer.AnalyzeAsync(image, "image/webp", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Same contract as the upload flow: a down provider must not turn
            // into a 500, it just yields a zero-confidence proposal.
            analysis = GarmentAnalysis.Empty();
        }

        GarmentAnalysisValidator.TryNormalize(analysis, out var proposal);
        return Results.Ok(proposal);
    }
}
