using FashionApp.Api.Infrastructure.Ai;
using FashionApp.Api.Infrastructure.Feeds;

namespace FashionApp.Api.Features.Digest;

/// <summary>
/// Trust boundary for AI-generated digests. Drafts are untrusted: URLs must
/// exist in the source feeds, tags must belong to <see cref="GarmentVocabulary"/>,
/// text is trimmed to the persisted column sizes, duplicates are removed and the
/// result is ordered and capped before the generator writes it.
/// </summary>
public static class DigestValidator
{
    public const int MaxTitleLength = 200;
    public const int MaxSummaryLength = 600;
    public const int MaxSourceLength = 80;
    public const int MaxStyleTags = 3;

    public static DigestSummary Clean(
        DigestSummary summary,
        IReadOnlyList<FeedEntry> sources,
        int maxItems)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(sources);

        var sourceUrls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (!string.IsNullOrWhiteSpace(source.Url))
                sourceUrls.Add(source.Url.Trim());
        }

        var seenUrls = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<DigestDraft>();

        foreach (var draft in summary.Items ?? [])
        {
            if (draft is null)
                continue;

            var url = draft.Url?.Trim() ?? "";
            if (url.Length == 0 || !sourceUrls.Contains(url))
                continue;

            if (!seenUrls.Add(url))
                continue;

            // "deja 1–3": a draft without a single vocabulary tag cannot relate
            // to the wardrobe and is discarded like an unknown URL.
            var tags = CleanTags(draft.StyleTags);
            if (tags.Length == 0)
                continue;

            kept.Add(new DigestDraft(
                Title: TrimTo(draft.Title, MaxTitleLength),
                Url: url,
                Source: TrimTo(draft.Source, MaxSourceLength),
                Summary: TrimTo(draft.Summary, MaxSummaryLength),
                StyleTags: tags,
                ImageUrl: string.IsNullOrWhiteSpace(draft.ImageUrl)
                    ? null
                    : draft.ImageUrl.Trim(),
                PublishedAt: draft.PublishedAt));
        }

        var items = kept
            .OrderByDescending(item => item.PublishedAt.HasValue)
            .ThenByDescending(item => item.PublishedAt)
            .Take(Math.Max(0, maxItems))
            .ToList();

        return new DigestSummary(
            TrimTo(summary.Summary, MaxSummaryLength) is { Length: > 0 } cleanSummary
                ? cleanSummary
                : null,
            items);
    }

    /// <summary>
    /// Keeps only vocabulary tags, normalizes them to the canonical lowercase
    /// form, removes duplicates and caps the list at three. An item without at
    /// least one valid tag is rejected by <see cref="Clean"/>.
    /// </summary>
    private static string[] CleanTags(string[]? tags)
    {
        if (tags is null || tags.Length == 0)
            return [];

        var result = new List<string>(MaxStyleTags);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in tags)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var normalized = raw.Trim().ToLowerInvariant();
            if (!GarmentVocabulary.StyleTags.Contains(normalized))
                continue;

            if (!seen.Add(normalized))
                continue;

            result.Add(normalized);
            if (result.Count == MaxStyleTags)
                break;
        }

        return [.. result];
    }

    private static string TrimTo(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength].TrimEnd();
    }
}
