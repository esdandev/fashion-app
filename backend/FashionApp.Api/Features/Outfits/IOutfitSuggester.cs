namespace FashionApp.Api.Features.Outfits;

public interface IOutfitSuggester
{
    Task<IReadOnlyList<OutfitDraft>> SuggestAsync(
        OutfitSuggestionContext context,
        CancellationToken ct);
}
