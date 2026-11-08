using FashionApp.Api.Features.Outfits;

namespace FashionApp.Api.Infrastructure.Ai;

/// <summary>
/// Produces outfit proposals from a compact wardrobe context. Implementations
/// may call a model (Gemini) or be deterministic (fake, rule-based); the result
/// is always untrusted and must pass <see cref="OutfitSuggestionValidator"/>.
/// </summary>
public interface IOutfitSuggester
{
    Task<IReadOnlyList<OutfitDraft>> SuggestAsync(
        OutfitSuggestionContext context,
        CancellationToken ct);
}
