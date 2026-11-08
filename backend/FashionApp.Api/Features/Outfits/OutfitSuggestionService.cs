using System.Globalization;
using System.Text.Json;
using FashionApp.Api.Domain;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Ai;
using FashionApp.Api.Infrastructure.Quotas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FashionApp.Api.Features.Outfits;

public enum OutfitSuggestionStatus
{
    Success,
    InvalidWeather,
    QuotaExceeded
}

/// <summary>
/// Outcome of a suggestion request. Invalid weather and an exhausted quota are
/// represented explicitly so the endpoint can map them to <c>400</c> and
/// <c>429</c> without exceptions.
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
/// Orchestrates one outfit suggestion: normalize the request, build the active
/// wardrobe context, serve from the in-memory cache when the wardrobe has not
/// changed, charge the suggestion quota on a miss, call the configured suggester
/// (with one retry), validate its drafts and fall back to the deterministic
/// rule-based suggester when nothing survives.
/// </summary>
public sealed class OutfitSuggestionService(
    AppDbContext db,
    IAiQuota quota,
    IMemoryCache cache,
    IOutfitSuggester suggester,
    RuleBasedOutfitSuggester fallback) : IOutfitSuggestionService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    private const string CachePrefix = "outfit-suggestions";

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

        // Project only the fields the suggestion needs. This is a single read
        // (no N+1) and the count + max UpdatedAt form the wardrobe fingerprint
        // of the cache key.
        var rows = await db.Garments
            .AsNoTracking()
            .Where(garment => !garment.IsArchived)
            .Select(garment => new WardrobeRow(
                garment.Id,
                garment.Category,
                garment.ColorName,
                garment.Formality,
                garment.Seasons,
                garment.StyleTagsJson,
                garment.UpdatedAt))
            .ToListAsync(ct);

        var maxUpdatedAt = rows.Count == 0 ? 0 : rows.Max(row => row.UpdatedAt).UtcTicks;
        var cacheKey = BuildCacheKey(
            normalizedOccasion,
            normalizedWeather,
            rows.Count,
            maxUpdatedAt);

        // A cache hit never costs quota and never calls the provider. This check
        // must stay before TryConsumeAsync.
        if (cache.TryGetValue(cacheKey, out OutfitSuggestionResult? cached)
            && cached is not null)
        {
            return cached;
        }

        if (!await quota.TryConsumeAsync(AiQuotaKinds.Suggestion, ct))
            return new OutfitSuggestionResult(OutfitSuggestionStatus.QuotaExceeded, []);

        var context = new OutfitSuggestionContext(
            normalizedOccasion,
            normalizedWeather,
            rows.Select(ToSummary).ToList());
        var wardrobe = rows.Select(ToGarment).ToList();

        // One retry covers transient provider failures; after that the shared
        // rule-based fallback must still return a usable answer, never a 500.
        var drafts = await TrySuggestAsync(suggester, context, wardrobe, ct);
        if (drafts.Count == 0)
            drafts = await TrySuggestAsync(suggester, context, wardrobe, ct);
        if (drafts.Count == 0)
            drafts = Normalize(await fallback.SuggestAsync(context, ct), wardrobe);

        var result = new OutfitSuggestionResult(OutfitSuggestionStatus.Success, drafts);
        cache.Set(cacheKey, result, CacheDuration);
        return result;
    }

    private static async Task<IReadOnlyList<OutfitDraft>> TrySuggestAsync(
        IOutfitSuggester suggester,
        OutfitSuggestionContext context,
        IReadOnlyList<Garment> wardrobe,
        CancellationToken ct)
    {
        try
        {
            var drafts = await suggester.SuggestAsync(context, ct);
            return Normalize(drafts ?? [], wardrobe);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }
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

    /// <summary>
    /// Applies the saveable limits before validation and then caps the answer at
    /// <see cref="OutfitDraftNormalizer.MaxDrafts"/>. Caching only ever stores
    /// this normalized result, so a cache hit is saveable too.
    /// </summary>
    private static IReadOnlyList<OutfitDraft> Normalize(
        IReadOnlyList<OutfitDraft> drafts,
        IReadOnlyList<Garment> wardrobe) =>
        OutfitSuggestionValidator
            .KeepValid(OutfitDraftNormalizer.NormalizeAll(drafts), wardrobe)
            .Take(OutfitDraftNormalizer.MaxDrafts)
            .ToList();

    private static string BuildCacheKey(
        string? occasion,
        string weather,
        int count,
        long maxUpdatedAtTicks) =>
        $"{CachePrefix}|{occasion ?? ""}|{weather}|{count}|" +
        maxUpdatedAtTicks.ToString(CultureInfo.InvariantCulture);

    private static GarmentSummary ToSummary(WardrobeRow row) => new(
        row.Id,
        row.Category.ToString().ToLowerInvariant(),
        row.ColorName,
        row.Formality,
        ParseSeasons(row.Seasons),
        ParseStyleTags(row.StyleTagsJson));

    /// <summary>
    /// Lightweight value carrier for <see cref="OutfitSuggestionValidator"/> and
    /// <see cref="OutfitRules"/>; it is never tracked or persisted.
    /// </summary>
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
        string StyleTagsJson,
        DateTimeOffset UpdatedAt);
}
