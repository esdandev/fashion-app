using FashionApp.Api.Infrastructure.Feeds;

namespace FashionApp.Api.Features.Digest;

public interface IDigestSummarizer
{
    Task<DigestSummary> SummarizeAsync(
        IReadOnlyList<FeedEntry> entries,
        CancellationToken ct);
}
