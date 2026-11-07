namespace FashionApp.Api.Infrastructure.Ai;

/// <summary>
/// Result of analyzing a garment photo. The upload flow maps it to a
/// <c>Garment</c> entity; Task 6 adds the real provider behind the interface.
/// </summary>
public sealed record GarmentAnalysis(
    string Name,
    string Category,
    string? Subcategory,
    string[] Colors,
    string? Pattern,
    string[] Seasons,
    int Formality,
    string? Material,
    string[] StyleTags,
    double Confidence)
{
    /// <summary>
    /// Neutral analysis used when the AI provider fails or returns nothing
    /// usable. It keeps the upload successful with <c>Confidence = 0</c>.
    /// </summary>
    public static GarmentAnalysis Empty() => new(
        Name: "Prenda",
        Category: "top",
        Subcategory: null,
        Colors: [],
        Pattern: null,
        Seasons: [],
        Formality: 1,
        Material: null,
        StyleTags: [],
        Confidence: 0);
}
