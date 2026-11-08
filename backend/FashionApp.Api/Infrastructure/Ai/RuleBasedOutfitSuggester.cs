using FashionApp.Api.Domain;
using FashionApp.Api.Features.Outfits;

namespace FashionApp.Api.Infrastructure.Ai;

/// <summary>
/// Deterministic fallback used when no AI suggestion survives validation, or
/// when the app runs without a model provider. It never invents garments: every
/// draft is built from the received wardrobe and checked with
/// <see cref="OutfitRules"/> before being returned.
/// </summary>
public sealed class RuleBasedOutfitSuggester : IOutfitSuggester
{
    private const int MaxSuggestions = 3;
    private const int PerCategoryLimit = 3;
    private const int DefaultFormality = 3;

    private static readonly IReadOnlyDictionary<string, int> OccasionFormality =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["trabajo"] = 4,
            ["cena"] = 4,
            ["casual"] = 2,
            ["fiesta"] = 3,
            ["deportivo"] = 1
        };

    public Task<IReadOnlyList<OutfitDraft>> SuggestAsync(
        OutfitSuggestionContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ct.ThrowIfCancellationRequested();

        var weather = NormalizeWeather(context.Weather);
        var occasion = string.IsNullOrWhiteSpace(context.Occasion)
            ? null
            : context.Occasion.Trim();
        var targetFormality = occasion is not null
            && OccasionFormality.TryGetValue(occasion, out var mapped)
                ? mapped
                : DefaultFormality;

        var wardrobe = (context.Wardrobe ?? [])
            .Select(ToRankedGarment)
            .OfType<RankedGarment>()
            .Where(garment => MatchesWeather(garment.Summary, weather))
            .ToList();

        var dresses = Closest(wardrobe, GarmentCategory.Dress, targetFormality);
        var tops = Closest(wardrobe, GarmentCategory.Top, targetFormality);
        var bottoms = Closest(wardrobe, GarmentCategory.Bottom, targetFormality);
        var shoes = Closest(wardrobe, GarmentCategory.Shoes, targetFormality);
        var outerwears = Closest(wardrobe, GarmentCategory.Outerwear, targetFormality);

        var candidates = new List<Candidate>();

        // A dress replaces separates, so it is a shape on its own.
        foreach (var dress in dresses)
        {
            foreach (var shoe in shoes)
            {
                AddShape(
                    candidates,
                    [dress, shoe],
                    outerwears,
                    dressShape: true,
                    shapeOrder: 0,
                    weather,
                    occasion,
                    targetFormality);
            }
        }

        foreach (var top in tops)
        {
            foreach (var bottom in bottoms)
            {
                foreach (var shoe in shoes)
                {
                    AddShape(
                        candidates,
                        [top, bottom, shoe],
                        outerwears,
                        dressShape: false,
                        shapeOrder: 1,
                        weather,
                        occasion,
                        targetFormality);
                }
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var suggestions = new List<OutfitDraft>(MaxSuggestions);

        foreach (var candidate in candidates
                     .OrderBy(c => c.Score)
                     .ThenBy(c => c.Order))
        {
            if (!seen.Add(candidate.Key))
                continue;

            suggestions.Add(candidate.Draft);
            if (suggestions.Count == MaxSuggestions)
                break;
        }

        return Task.FromResult<IReadOnlyList<OutfitDraft>>(suggestions);
    }

    private static void AddShape(
        List<Candidate> candidates,
        IReadOnlyList<RankedGarment> basePieces,
        IReadOnlyList<RankedGarment> outerwears,
        bool dressShape,
        int shapeOrder,
        string weather,
        string? occasion,
        int targetFormality)
    {
        // When it is cold, an outerwear is expected. If none of the available
        // outerwears can be combined without breaking the rules, the base outfit
        // is still better than no suggestion at all.
        if (weather == "frio" && outerwears.Count > 0)
        {
            var addedWithOuterwear = false;
            foreach (var outerwear in outerwears)
            {
                List<RankedGarment> pieces = [.. basePieces, outerwear];
                addedWithOuterwear |= TryAdd(
                    candidates,
                    pieces,
                    dressShape,
                    shapeOrder,
                    hasOuterwear: true,
                    weather,
                    occasion,
                    targetFormality);
            }

            if (addedWithOuterwear)
                return;
        }

        TryAdd(
            candidates,
            basePieces,
            dressShape,
            shapeOrder,
            hasOuterwear: false,
            weather,
            occasion,
            targetFormality);
    }

    private static bool TryAdd(
        List<Candidate> candidates,
        IReadOnlyList<RankedGarment> pieces,
        bool dressShape,
        int shapeOrder,
        bool hasOuterwear,
        string weather,
        string? occasion,
        int targetFormality)
    {
        var garments = pieces.Select(piece => piece.Garment).ToList();
        if (!OutfitRules.Validate(garments).IsValid)
            return false;

        var ids = pieces.Select(piece => piece.Summary.Id).ToArray();
        var score = pieces.Sum(
            piece => Math.Abs(piece.Summary.Formality - targetFormality));
        var draft = new OutfitDraft(
            dressShape ? "Look con vestido" : "Look de dos piezas",
            ids,
            occasion,
            BuildRationale(dressShape, hasOuterwear, weather, occasion));

        candidates.Add(new Candidate(BuildKey(ids), score, shapeOrder, draft));
        return true;
    }

    private static List<RankedGarment> Closest(
        IEnumerable<RankedGarment> wardrobe,
        GarmentCategory category,
        int targetFormality) =>
        wardrobe
            .Where(garment => garment.Garment.Category == category)
            .OrderBy(garment => Math.Abs(garment.Summary.Formality - targetFormality))
            .ThenBy(garment => garment.Summary.Id)
            .Take(PerCategoryLimit)
            .ToList();

    private static RankedGarment? ToRankedGarment(GarmentSummary? summary)
    {
        if (summary is null)
            return null;

        if (!Enum.TryParse<GarmentCategory>(summary.Category, ignoreCase: true, out var category)
            || !Enum.IsDefined(category))
            return null;

        var garment = new Garment
        {
            Id = summary.Id,
            Category = category,
            Formality = summary.Formality,
            Seasons = string.Join(',', summary.Seasons ?? [])
        };

        return new RankedGarment(summary, garment);
    }

    private static bool MatchesWeather(GarmentSummary summary, string weather)
    {
        if (weather == "templado")
            return true;

        var seasons = summary.Seasons ?? [];
        return weather switch
        {
            "calor" => seasons.Any(
                season => season.Equals("summer", StringComparison.OrdinalIgnoreCase)),
            "frio" => seasons.Any(
                season => season.Equals("winter", StringComparison.OrdinalIgnoreCase)
                          || season.Equals("autumn", StringComparison.OrdinalIgnoreCase)),
            _ => true
        };
    }

    private static string NormalizeWeather(string? weather) =>
        weather?.Trim().ToLowerInvariant() switch
        {
            "frio" => "frio",
            "calor" => "calor",
            _ => "templado"
        };

    private static string BuildRationale(
        bool dressShape,
        bool hasOuterwear,
        string weather,
        string? occasion)
    {
        var weatherLabel = weather switch
        {
            "frio" => "frío",
            "calor" => "calor",
            _ => "templado"
        };

        var baseText = (dressShape, hasOuterwear) switch
        {
            (true, true) => "Vestido, calzado y abrigo",
            (true, false) => "Vestido y calzado",
            (false, true) => "Parte superior, inferior, calzado y abrigo",
            (false, false) => "Parte superior, inferior y calzado"
        };

        var occasionText = occasion is null ? "" : $" para {occasion}";
        return $"{baseText} acordes al clima {weatherLabel}{occasionText}.";
    }

    private static string BuildKey(IEnumerable<Guid> ids) =>
        string.Join(
            ",",
            ids.OrderBy(id => id).Select(id => id.ToString("N")));

    private sealed record RankedGarment(GarmentSummary Summary, Garment Garment);

    private sealed record Candidate(string Key, int Score, int Order, OutfitDraft Draft);
}
