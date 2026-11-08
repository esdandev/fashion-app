namespace FashionApp.Api.Features.Outfits;

/// <summary>
/// Normalizes untrusted outfit proposals at the service boundary. The limits
/// mirror the manual create endpoint so every draft returned (and cached) by
/// the suggestion service can be saved through <c>POST /api/outfits</c> without
/// a <c>400</c> caused by an overlong text or too many pieces.
/// </summary>
public static class OutfitDraftNormalizer
{
    public const string FallbackName = "Outfit sugerido";

    public const int MaxNameLength = 120;

    public const int MaxOccasionLength = 60;

    public const int MaxRationaleLength = 600;

    public const int MaxGarmentIds = 5;

    public const int MaxDrafts = 3;

    public static OutfitDraft Normalize(OutfitDraft draft)
    {
        var name = ClampRequired(draft.Name, MaxNameLength);
        if (name.Length == 0)
            name = FallbackName;

        var occasion = ClampOptional(draft.Occasion, MaxOccasionLength);
        var rationale = ClampRequired(draft.Rationale, MaxRationaleLength);
        var garmentIds = (draft.GarmentIds ?? [])
            .Take(MaxGarmentIds)
            .ToArray();

        return new OutfitDraft(name, garmentIds, occasion, rationale);
    }

    /// <summary>
    /// Trims every draft and caps its ids before validation. A draft whose base
    /// pieces no longer compose a valid outfit after that cap is dropped by
    /// <see cref="OutfitSuggestionValidator"/>.
    /// </summary>
    public static IReadOnlyList<OutfitDraft> NormalizeAll(
        IReadOnlyList<OutfitDraft> drafts) =>
        drafts.Select(Normalize).ToList();

    private static string ClampRequired(string? value, int maxLength)
    {
        var trimmed = (value ?? "").Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength].TrimEnd();
    }

    private static string? ClampOptional(string? value, int maxLength)
    {
        var clamped = ClampRequired(value, maxLength);
        return clamped.Length == 0 ? null : clamped;
    }
}
