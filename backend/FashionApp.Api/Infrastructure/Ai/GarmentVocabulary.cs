namespace FashionApp.Api.Infrastructure.Ai;

/// <summary>
/// Closed vocabularies and structural bounds shared by the AI validator and the
/// user-input rules. Keeping one list means a tag accepted from the model is
/// also accepted from the form, and vice versa.
/// </summary>
public static class GarmentVocabulary
{
    public const int MaxNameLength = 120;
    public const int MaxSubcategoryLength = 60;
    public const int MaxColorLength = 40;
    public const int MaxColorHexLength = 7;
    public const int MaxPatternLength = 40;
    public const int MaxMaterialLength = 60;
    public const int MaxNotesLength = 400;
    public const int MaxColors = 4;
    public const int MaxStyleTags = 4;

    public static readonly IReadOnlySet<string> Categories = new HashSet<string>(
        ["top", "bottom", "dress", "outerwear", "shoes", "bag", "accessory"],
        StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlySet<string> Seasons = new HashSet<string>(
        ["spring", "summer", "autumn", "winter"],
        StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlySet<string> StyleTags = new HashSet<string>(
        [
            "minimalista", "clasico", "casual", "streetwear", "romantico", "bohemio",
            "deportivo", "formal", "vintage", "noventoso", "y2k", "utilitario",
            "monocromatico", "colorido", "oversized", "entallado", "capas", "texturado"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static bool IsValidSeason(string? value) =>
        value is not null && Seasons.Contains(value.Trim());

    public static bool IsValidStyleTag(string? value) =>
        value is not null && StyleTags.Contains(value.Trim());
}
