using FashionApp.Api.Features.Security;
using FashionApp.Api.Infrastructure.Quotas;
using FashionApp.Api.Infrastructure.RateLimiting;

namespace FashionApp.Api.Features.Outfits;

/// <summary>
/// <c>POST /api/outfits/suggest</c>: returns validated outfit drafts for an
/// occasion and weather. Nothing is persisted and the demo never reaches the
/// handler because writes are blocked before the token filter.
/// </summary>
public static class OutfitSuggestEndpoint
{
    public static IEndpointRouteBuilder MapOutfitSuggestEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/outfits/suggest", HandleAsync)
            .BlockWritesInDemo()
            .RequireToken()
            .RequireRateLimiting(ClientRateLimiting.Policy);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        SuggestOutfitsRequest? request,
        IOutfitSuggestionService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.SuggestAsync(
            request?.Occasion,
            request?.Weather ?? "",
            cancellationToken);

        return result.Status switch
        {
            OutfitSuggestionStatus.InvalidWeather => InvalidWeather(),
            OutfitSuggestionStatus.QuotaExceeded =>
                QuotaResults.TooManyRequests(httpContext, AiQuotaKinds.Suggestion),
            _ => Results.Ok(new SuggestOutfitsResponse(result.Outfits))
        };
    }

    private static IResult InvalidWeather() => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Clima inválido",
        detail: "El clima debe ser 'frio', 'templado' o 'calor'.");
}
