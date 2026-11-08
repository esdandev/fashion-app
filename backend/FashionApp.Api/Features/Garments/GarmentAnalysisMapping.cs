using System.Text.Json;
using FashionApp.Api.Domain;
using FashionApp.Api.Infrastructure.Ai;

namespace FashionApp.Api.Features.Garments;

/// <summary>
/// Converts a stored <see cref="Garment"/> back into the analysis shape used by
/// the analyze endpoint. The original model answer is preferred; older rows
/// without <c>AnalysisJson</c> fall back to their confirmed columns.
/// </summary>
internal static class GarmentAnalysisMapping
{
    public static GarmentAnalysis FromGarment(Garment garment)
    {
        GarmentAnalysis? stored = null;

        if (!string.IsNullOrWhiteSpace(garment.AnalysisJson))
        {
            try
            {
                stored = JsonSerializer.Deserialize<GarmentAnalysis>(garment.AnalysisJson);
            }
            catch (JsonException)
            {
                // Corrupt or legacy JSON: rebuild from the confirmed columns.
            }
        }

        var fallback = new GarmentAnalysis(
            Name: garment.Name,
            Category: CategoryToApi(garment.Category),
            Subcategory: garment.Subcategory,
            Colors: string.IsNullOrWhiteSpace(garment.ColorName)
                ? []
                : [garment.ColorName],
            Pattern: garment.Pattern,
            Seasons: SplitList(garment.Seasons),
            Formality: garment.Formality,
            Material: garment.Material,
            StyleTags: ParseStyleTags(garment.StyleTagsJson),
            Confidence: garment.Confidence);

        GarmentAnalysisValidator.TryNormalize(stored ?? fallback, out var normalized);
        return normalized;
    }

    public static string CategoryToApi(GarmentCategory category) =>
        category.ToString().ToLowerInvariant();

    public static GarmentCategory ParseCategory(
        string? category,
        GarmentCategory fallback = GarmentCategory.Top)
    {
        var value = category?.Trim();
        return Enum.TryParse<GarmentCategory>(value, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            ? parsed
            : fallback;
    }

    public static string[] SplitList(string? values) =>
        string.IsNullOrWhiteSpace(values)
            ? []
            : values.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string[] ParseStyleTags(string? styleTagsJson)
    {
        if (string.IsNullOrWhiteSpace(styleTagsJson))
            return [];

        try
        {
            return JsonSerializer.Deserialize<string[]>(styleTagsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
