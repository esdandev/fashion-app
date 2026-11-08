namespace FashionApp.Api.Infrastructure.Ai;

/// <summary>
/// Normalizes a raw model answer against the closed vocabularies of the
/// product spec. Invalid categories fall back to <c>top</c>, formality is
/// clamped to 1..5, unknown seasons and style tags are dropped, and every
/// string is trimmed to the length allowed by the <c>Garment</c> model.
/// </summary>
public static class GarmentAnalysisValidator
{
    private const int MaxNameLength = GarmentVocabulary.MaxNameLength;
    private const int MaxSubcategoryLength = GarmentVocabulary.MaxSubcategoryLength;
    private const int MaxColorLength = GarmentVocabulary.MaxColorLength;
    private const int MaxPatternLength = GarmentVocabulary.MaxPatternLength;
    private const int MaxMaterialLength = GarmentVocabulary.MaxMaterialLength;
    private const int MaxColors = GarmentVocabulary.MaxColors;
    private const int MaxStyleTags = GarmentVocabulary.MaxStyleTags;

    /// <summary>
    /// Returns <c>false</c> only when <paramref name="input"/> is null; every
    /// other value is normalized and accepted so a partial model answer can
    /// still be stored with a lower confidence instead of failing the upload.
    /// </summary>
    public static bool TryNormalize(GarmentAnalysis input, out GarmentAnalysis normalized)
    {
        if (input is null)
        {
            normalized = GarmentAnalysis.Empty();
            return false;
        }

        var name = TrimToLength(input.Name, MaxNameLength);
        if (name.Length == 0)
            name = "Prenda";

        normalized = new GarmentAnalysis(
            Name: name,
            Category: NormalizeCategory(input.Category),
            Subcategory: TrimToLengthOrNull(input.Subcategory, MaxSubcategoryLength),
            Colors: NormalizeColors(input.Colors),
            Pattern: TrimToLengthOrNull(input.Pattern, MaxPatternLength),
            Seasons: NormalizeVocabulary(input.Seasons, GarmentVocabulary.Seasons),
            Formality: Math.Clamp(input.Formality, 1, 5),
            Material: TrimToLengthOrNull(input.Material, MaxMaterialLength),
            StyleTags: NormalizeVocabulary(input.StyleTags, GarmentVocabulary.StyleTags, MaxStyleTags),
            Confidence: double.IsFinite(input.Confidence)
                ? Math.Clamp(input.Confidence, 0, 1)
                : 0);

        return true;
    }

    private static string NormalizeCategory(string? category)
    {
        var value = category?.Trim() ?? "";
        return GarmentVocabulary.Categories.Contains(value) ? value.ToLowerInvariant() : "top";
    }

    private static string[] NormalizeColors(string[]? colors)
    {
        if (colors is null)
            return [];

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var color in colors)
        {
            if (result.Count >= MaxColors)
                break;

            var value = TrimToLength(color, MaxColorLength);
            if (value.Length > 0 && seen.Add(value))
                result.Add(value);
        }

        return [.. result];
    }

    private static string[] NormalizeVocabulary(
        string[]? values,
        IReadOnlySet<string> vocabulary,
        int maxCount = int.MaxValue)
    {
        if (values is null)
            return [];

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values)
        {
            if (result.Count >= maxCount)
                break;

            var candidate = value?.Trim();
            if (string.IsNullOrEmpty(candidate) || !vocabulary.Contains(candidate))
                continue;

            var canonical = candidate.ToLowerInvariant();
            if (seen.Add(canonical))
                result.Add(canonical);
        }

        return [.. result];
    }

    private static string TrimToLength(string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? "";
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd();
    }

    private static string? TrimToLengthOrNull(string? value, int maxLength)
    {
        var trimmed = TrimToLength(value, maxLength);
        return trimmed.Length == 0 ? null : trimmed;
    }
}
