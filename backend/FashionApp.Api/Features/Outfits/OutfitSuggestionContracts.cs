namespace FashionApp.Api.Features.Outfits;

/// <summary>
/// Compact, serializable view of a garment handed to an outfit suggester. It
/// carries only the fields the model (or the rule-based fallback) needs, so the
/// wardrobe never leaves the backend as full domain entities.
/// </summary>
public sealed record GarmentSummary(
    Guid Id,
    string Category,
    string ColorName,
    int Formality,
    string[] Seasons,
    string[] StyleTags);

/// <summary>
/// Everything a suggester needs to propose outfits: the requested occasion, the
/// normalized weather and the active wardrobe.
/// </summary>
public sealed record OutfitSuggestionContext(
    string? Occasion,
    string Weather,
    IReadOnlyList<GarmentSummary> Wardrobe);

/// <summary>
/// A proposed outfit before persistence. It is only a suggestion: the caller
/// resolves <see cref="GarmentIds"/> against the active wardrobe and validates
/// the composition with <see cref="OutfitSuggestionValidator"/>.
/// </summary>
public sealed record OutfitDraft(
    string Name,
    Guid[] GarmentIds,
    string? Occasion,
    string Rationale);

/// <summary>
/// Request body for <c>POST /api/outfits/suggest</c>. The weather is optional:
/// a missing or blank value defaults to <c>templado</c>, while an explicitly
/// unknown value maps to a <c>400</c>.
/// </summary>
public sealed record SuggestOutfitsRequest(string? Occasion, string? Weather);

/// <summary>
/// Response envelope for <c>POST /api/outfits/suggest</c>. Drafts are not
/// persisted: the client decides which ones to save through the manual CRUD.
/// </summary>
public sealed record SuggestOutfitsResponse(IReadOnlyList<OutfitDraft> Outfits);
