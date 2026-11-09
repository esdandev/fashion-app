using System.Text.Json;
using FashionApp.Api.Domain;
using FashionApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FashionApp.Api.Features.Outfits;

public enum OutfitSuggestionStatus
{
    Success,
    InvalidWeather
}

/// <summary>
/// Outcome of a suggestion request. Invalid weather is represented explicitly
/// so the endpoint can map it to <c>400</c> without exceptions.
/// </summary>
public sealed record OutfitSuggestionResult(
    OutfitSuggestionStatus Status,
    IReadOnlyList<OutfitDraft> Outfits);

public interface IOutfitSuggestionService
{
    Task<OutfitSuggestionResult> SuggestAsync(
        string? occasion,
        string weather,
        CancellationToken ct);
}

/// <summary>
/// Deterministic outfit suggestions: reads the active wardrobe, asks the
/// rule-based suggester and validates its drafts. No AI, no cache, no quota.
/// </summary>
public sealed class OutfitSuggestionService(
    AppDbContext db,
    IOutfitSuggester suggester) : IOutfitSuggestionService
{
    public async Task<OutfitSuggestionResult> SuggestAsync(
        string? occasion,
        string weather,
        CancellationToken ct)
    {
        var normalizedWeather = NormalizeWeather(weather);
        if (normalizedWeather is null)
            return new OutfitSuggestionResult(OutfitSuggestionStatus.InvalidWeather, []);

        var normalizedOccasion = string.IsNullOrWhiteSpace(occasion)
            ? null
            : occasion.Trim();

        var rows = await db.Garments
            .AsNoTracking()
            .Where(garment => !garment.IsArchived)
            .Select(garment => new WardrobeRow(
                garment.Id,
                garment.Category,
                garment.ColorName,
                garment.Formality,
                garment.Seasons,
                garment.StyleTagsJson))
            .ToListAsync(ct);

        var context = new OutfitSuggestionContext(
            normalizedOccasion,
            normalizedWeather,
            rows.Select(ToSummary).ToList());
        var wardrobe = rows.Select(ToGarment).ToList();

        var drafts = await suggester.SuggestAsync(context, ct);
        var valid = OutfitSuggestionValidator
            .KeepValid(drafts, wardrobe)
            .Take(3)
            .ToList();

        return new OutfitSuggestionResult(OutfitSuggestionStatus.Success, valid);
    }

    /// <summary>
    /// The spec makes the weather optional; a missing or blank value defaults to
    /// the temperate case. An explicitly unknown value is still invalid.
    /// </summary>
    private static string? NormalizeWeather(string? weather)
    {
        if (string.IsNullOrWhiteSpace(weather))
            return "templado";

        return weather.Trim().ToLowerInvariant() switch
        {
            "frio" or "frío" => "frio",
            "templado" => "templado",
            "calor" => "calor",
            _ => null
        };
    }

    private static GarmentSummary ToSummary(WardrobeRow row) => new(
        row.Id,
        row.Category.ToString().ToLowerInvariant(),
        row.ColorName,
        row.Formality,
        ParseSeasons(row.Seasons),
        ParseStyleTags(row.StyleTagsJson));

    private static Garment ToGarment(WardrobeRow row) => new()
    {
        Id = row.Id,
        Category = row.Category,
        Formality = row.Formality,
        Seasons = row.Seasons ?? ""
    };

    private static string[] ParseSeasons(string? seasons) =>
        string.IsNullOrWhiteSpace(seasons)
            ? []
            : seasons.Split(
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

    private sealed record WardrobeRow(
        Guid Id,
        GarmentCategory Category,
        string ColorName,
        int Formality,
        string Seasons,
        string StyleTagsJson);
}
