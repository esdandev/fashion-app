using FashionApp.Api.Infrastructure.Feeds;

namespace FashionApp.Api.Features.Digest;

/// <summary>
/// Deterministic digest summarizer: keeps the feed's own title and summary and
/// infers style tags from a small keyword table. No AI, no network, no cost.
/// </summary>
public sealed class RuleBasedDigestSummarizer : IDigestSummarizer
{
    private const int MaxItems = 10;
    private const int MaxTitleLength = 200;
    private const int MaxSummaryLength = 600;
    private const int MaxSourceLength = 80;

    private static readonly (string Tag, string[] Words)[] TagRules =
    [
        ("minimalista", ["minimal", "básic", "basico", "esencial"]),
        ("casual", ["casual", "diario", "informal"]),
        ("formal", ["formal", "sastr", "oficina", "trabajo"]),
        ("streetwear", ["street", "urbano", "hoodie"]),
        ("vintage", ["vintage", "retro"]),
        ("deportivo", ["deportiv", "sport", "running"]),
        ("romantico", ["romántic", "romantic", "floral", "encaje"]),
        ("bohemio", ["boho", "bohem"]),
        ("colorido", ["color", "estampado", "print"]),
        ("capas", ["capa", "layering", "abrigo"]),
        ("texturado", ["textura", "tweed", "lana", "punto"]),
        ("monocromatico", ["monocrom", "total black", "total white"])
    ];

    public Task<DigestSummary> SummarizeAsync(
        IReadOnlyList<FeedEntry> entries,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ct.ThrowIfCancellationRequested();

        var drafts = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Url))
            .Take(MaxItems)
            .Select(entry => new DigestDraft(
                Title: Truncate(entry.Title, MaxTitleLength),
                Url: entry.Url.Trim(),
                Source: Truncate(entry.Source, MaxSourceLength),
                Summary: BuildSummary(entry),
                StyleTags: InferTags(entry),
                ImageUrl: entry.ImageUrl,
                PublishedAt: entry.PublishedAt))
            .ToList();

        var summary = drafts.Count == 0
            ? null
            : $"{drafts.Count} novedades de moda para hoy.";

        return Task.FromResult(new DigestSummary(summary, drafts));
    }

    private static string BuildSummary(FeedEntry entry)
    {
        var text = string.IsNullOrWhiteSpace(entry.Summary)
            ? $"Novedad de {entry.Source}."
            : entry.Summary.Trim();

        return Truncate(text, MaxSummaryLength);
    }

    private static string[] InferTags(FeedEntry entry)
    {
        var text = $"{entry.Title} {entry.Summary}".ToLowerInvariant();
        var tags = new List<string>();

        foreach (var (tag, words) in TagRules)
        {
            if (words.Any(text.Contains))
            {
                tags.Add(tag);
                if (tags.Count == 3)
                    break;
            }
        }

        if (tags.Count == 0)
            tags.Add("casual");

        return [.. tags];
    }

    private static string Truncate(string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? "";
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
