using FashionApp.Api.Features.Digest;
using FashionApp.Api.Infrastructure.Feeds;

namespace FashionApp.Api.Infrastructure.Ai;

/// <summary>
/// Deterministic summarizer used in tests and local development. It emits one
/// draft per entry so the whole digest flow can run without network calls; the
/// drafts still pass through <see cref="DigestValidator"/>.
/// </summary>
public sealed class FakeDigestSummarizer : IDigestSummarizer
{
    public const string FixedSummary = "Resumen de prueba";

    private const int MaxTitleLength = 200;
    private const int MaxItemSummaryLength = 600;

    public Task<DigestSummary> SummarizeAsync(
        IReadOnlyList<FeedEntry> entries,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ct.ThrowIfCancellationRequested();

        var drafts = entries
            .Select((entry, index) => new DigestDraft(
                Title: TrimTo(entry.Title, MaxTitleLength),
                Url: entry.Url,
                Source: entry.Source,
                Summary: TrimTo(
                    entry.Summary ?? $"Un adelanto de {entry.Title}.",
                    MaxItemSummaryLength),
                StyleTags: index == 0 ? ["minimalista", "casual"] : ["casual"],
                ImageUrl: entry.ImageUrl,
                PublishedAt: entry.PublishedAt))
            .ToList();

        return Task.FromResult(new DigestSummary(FixedSummary, drafts));
    }

    private static string TrimTo(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd();
    }
}
