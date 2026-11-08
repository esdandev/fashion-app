namespace FashionApp.Api.Infrastructure.Feeds;

/// <summary>
/// A configured RSS/Atom feed. <see cref="Name"/> is the label shown as the
/// source of every entry; <see cref="Url"/> is the feed endpoint.
/// </summary>
public sealed record FeedSource(string Name, string Url);

/// <summary>
/// One entry read from a feed. Only the title, link, a short summary and the
/// optional image are kept: the article body is never copied.
/// </summary>
public sealed record FeedEntry(
    string Title,
    string Url,
    string Source,
    DateTimeOffset? PublishedAt,
    string? Summary,
    string? ImageUrl);

public interface IFeedFetcher
{
    Task<IReadOnlyList<FeedEntry>> FetchAsync(FeedSource source, CancellationToken ct);
}
