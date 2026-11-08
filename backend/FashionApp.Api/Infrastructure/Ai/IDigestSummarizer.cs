using FashionApp.Api.Features.Digest;
using FashionApp.Api.Infrastructure.Feeds;

namespace FashionApp.Api.Infrastructure.Ai;

public interface IDigestSummarizer
{
    Task<DigestSummary> SummarizeAsync(IReadOnlyList<FeedEntry> entries, CancellationToken ct);
}
