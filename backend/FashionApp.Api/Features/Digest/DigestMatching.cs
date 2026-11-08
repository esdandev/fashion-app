using System.Text.Json;
using FashionApp.Api.Domain;
using FashionApp.Api.Features.Garments;

namespace FashionApp.Api.Features.Digest;

public static class DigestMatching
{
    /// <summary>
    /// Resolves the wardrobe pieces that share style tags with a digest item.
    /// Only active garments with at least one shared tag are considered; the
    /// result is ordered by number of matches (then newest, then name) and
    /// capped at <paramref name="max"/>.
    /// </summary>
    public static IReadOnlyList<GarmentDto> Match(
        IReadOnlyList<Garment> active,
        IReadOnlyList<string> tags,
        int max = 3)
    {
        if (active.Count == 0 || max <= 0)
            return [];

        var wanted = tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (wanted.Count == 0)
            return [];

        return active
            .Select(garment => (Garment: garment, Matches: CountMatches(garment.StyleTagsJson, wanted)))
            .Where(candidate => candidate.Matches > 0)
            .OrderByDescending(candidate => candidate.Matches)
            .ThenByDescending(candidate => candidate.Garment.UpdatedAt)
            .ThenBy(candidate => candidate.Garment.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(candidate => GarmentQuery.ToDto(candidate.Garment))
            .ToList();
    }

    private static int CountMatches(string? styleTagsJson, HashSet<string> wanted)
    {
        if (string.IsNullOrWhiteSpace(styleTagsJson))
            return 0;

        try
        {
            var garmentTags = JsonSerializer.Deserialize<string[]>(styleTagsJson);
            if (garmentTags is null)
                return 0;

            return garmentTags.Count(tag =>
                !string.IsNullOrWhiteSpace(tag) && wanted.Contains(tag.Trim()));
        }
        catch (JsonException)
        {
            // A corrupt JSON column must never break the public digest.
            return 0;
        }
    }
}
