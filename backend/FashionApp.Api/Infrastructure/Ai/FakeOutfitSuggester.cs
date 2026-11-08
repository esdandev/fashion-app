using FashionApp.Api.Domain;
using FashionApp.Api.Features.Outfits;

namespace FashionApp.Api.Infrastructure.Ai;

/// <summary>
/// Deterministic suggester used in tests and local development. It never calls a
/// provider: it builds up to two distinct proposals from the received wardrobe
/// (dress + shoes and/or top + bottom + shoes) and re-checks every combination
/// with <see cref="OutfitRules"/> so the fake cannot emit an invalid outfit.
/// </summary>
public sealed class FakeOutfitSuggester : IOutfitSuggester
{
    private const int MaxSuggestions = 2;
    private const int PerCategoryLimit = 2;

    public Task<IReadOnlyList<OutfitDraft>> SuggestAsync(
        OutfitSuggestionContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ct.ThrowIfCancellationRequested();

        var occasion = string.IsNullOrWhiteSpace(context.Occasion)
            ? null
            : context.Occasion.Trim();

        var wardrobe = (context.Wardrobe ?? [])
            .Select(ToGarment)
            .OfType<Garment>()
            .OrderBy(garment => garment.Id)
            .ToList();

        var byCategory = wardrobe
            .GroupBy(garment => garment.Category)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Garment>)group.Take(PerCategoryLimit).ToList());

        var suggestions = new List<OutfitDraft>(MaxSuggestions);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void TryAdd(string name, params Garment[] pieces)
        {
            if (suggestions.Count >= MaxSuggestions)
                return;

            if (!OutfitRules.Validate(pieces).IsValid)
                return;

            var ids = pieces.Select(piece => piece.Id).ToArray();
            if (!seen.Add(BuildKey(ids)))
                return;

            suggestions.Add(new OutfitDraft(
                name,
                ids,
                occasion,
                $"Propuesta de ejemplo para {OccasionLabel(occasion)} con clima {WeatherLabel(context.Weather)}."));
        }

        foreach (var dress in Category(byCategory, GarmentCategory.Dress))
        {
            foreach (var shoes in Category(byCategory, GarmentCategory.Shoes))
                TryAdd("Propuesta con vestido", dress, shoes);
        }

        foreach (var top in Category(byCategory, GarmentCategory.Top))
        {
            foreach (var bottom in Category(byCategory, GarmentCategory.Bottom))
            {
                foreach (var shoes in Category(byCategory, GarmentCategory.Shoes))
                    TryAdd("Propuesta de dos piezas", top, bottom, shoes);
            }
        }

        return Task.FromResult<IReadOnlyList<OutfitDraft>>(suggestions);
    }

    private static IReadOnlyList<Garment> Category(
        IReadOnlyDictionary<GarmentCategory, IReadOnlyList<Garment>> byCategory,
        GarmentCategory category) =>
        byCategory.TryGetValue(category, out var garments) ? garments : [];

    private static Garment? ToGarment(GarmentSummary? summary)
    {
        if (summary is null)
            return null;

        if (!Enum.TryParse<GarmentCategory>(summary.Category, ignoreCase: true, out var category)
            || !Enum.IsDefined(category))
            return null;

        return new Garment
        {
            Id = summary.Id,
            Category = category,
            Formality = summary.Formality,
            Seasons = string.Join(',', summary.Seasons ?? [])
        };
    }

    private static string BuildKey(IEnumerable<Guid> ids) =>
        string.Join(
            ",",
            ids.OrderBy(id => id).Select(id => id.ToString("N")));

    private static string OccasionLabel(string? occasion) =>
        occasion is null ? "cualquier ocasión" : occasion;

    private static string WeatherLabel(string? weather) =>
        weather?.Trim().ToLowerInvariant() switch
        {
            "frio" => "frío",
            "calor" => "caluroso",
            _ => "templado"
        };
}
