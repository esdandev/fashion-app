using System.Text.RegularExpressions;
using FashionApp.Api.Infrastructure.Ai;

namespace FashionApp.Api.Features.Garments;

/// <summary>
/// Rejects user input that is outside the product's closed vocabularies and
/// column bounds. Unlike <see cref="GarmentAnalysisValidator"/>, which repairs a
/// model answer, form/PATCH input is rejected with a Spanish message so the user
/// never thinks a tag or season was saved when it was silently dropped.
/// </summary>
public static partial class GarmentInputRules
{
    public static string? Validate(
        string? name,
        bool requireName,
        string? category,
        string? subcategory,
        string? colorName,
        string? colorHex,
        string? pattern,
        string? material,
        string? notes,
        int? formality,
        IReadOnlyCollection<string>? seasons,
        IReadOnlyCollection<string>? styleTags)
    {
        if (requireName || name is not null)
        {
            var trimmedName = name?.Trim() ?? "";
            if (trimmedName.Length == 0)
                return "El nombre no puede estar vacío.";
            if (trimmedName.Length > GarmentVocabulary.MaxNameLength)
                return $"El nombre no puede superar los {GarmentVocabulary.MaxNameLength} caracteres.";
        }

        if (category is not null && !GarmentVocabulary.Categories.Contains(category.Trim()))
            return "La categoría no es válida.";

        if (formality is int value && (value < 1 || value > 5))
            return "La formalidad debe estar entre 1 y 5.";

        if (!string.IsNullOrWhiteSpace(colorHex)
            && !ColorHexPattern().IsMatch(colorHex.Trim()))
        {
            return "El color hexadecimal debe tener el formato #RRGGBB.";
        }

        var lengthError =
            CheckLength("La subcategoría", subcategory, GarmentVocabulary.MaxSubcategoryLength)
            ?? CheckLength("El color", colorName, GarmentVocabulary.MaxColorLength)
            ?? CheckLength("El patrón", pattern, GarmentVocabulary.MaxPatternLength)
            ?? CheckLength("El material", material, GarmentVocabulary.MaxMaterialLength)
            ?? CheckLength("Las notas", notes, GarmentVocabulary.MaxNotesLength);

        if (lengthError is not null)
            return lengthError;

        if (seasons is not null)
        {
            foreach (var season in seasons)
            {
                var candidate = season?.Trim();
                if (string.IsNullOrEmpty(candidate))
                    continue;

                if (!GarmentVocabulary.IsValidSeason(candidate))
                {
                    return $"La temporada '{candidate}' no es válida. " +
                        "Usá spring, summer, autumn o winter.";
                }
            }
        }

        if (styleTags is not null)
        {
            var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in styleTags)
            {
                var candidate = tag?.Trim();
                if (string.IsNullOrEmpty(candidate))
                    continue;

                if (!GarmentVocabulary.IsValidStyleTag(candidate))
                {
                    return $"La etiqueta '{candidate}' no es válida. " +
                        "Elegí etiquetas del vocabulario controlado.";
                }

                normalized.Add(candidate.ToLowerInvariant());
            }

            if (normalized.Count > GarmentVocabulary.MaxStyleTags)
            {
                return $"Podés elegir hasta {GarmentVocabulary.MaxStyleTags} etiquetas.";
            }
        }

        return null;
    }

    public static string[] NormalizeSeasons(IEnumerable<string>? values) =>
        NormalizeVocabulary(values, GarmentVocabulary.Seasons);

    public static string[] NormalizeStyleTags(IEnumerable<string>? values) =>
        NormalizeVocabulary(values, GarmentVocabulary.StyleTags);

    private static string[] NormalizeVocabulary(
        IEnumerable<string>? values,
        IReadOnlySet<string> vocabulary)
    {
        if (values is null)
            return [];

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values)
        {
            var candidate = value?.Trim();
            if (string.IsNullOrEmpty(candidate) || !vocabulary.Contains(candidate))
                continue;

            var canonical = candidate.ToLowerInvariant();
            if (seen.Add(canonical))
                result.Add(canonical);
        }

        return [.. result];
    }

    private static string? CheckLength(string label, string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? "";
        return trimmed.Length > maxLength
            ? $"{label} no puede superar los {maxLength} caracteres."
            : null;
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorHexPattern();
}
