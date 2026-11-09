namespace FashionApp.Api.Infrastructure.Feeds;

/// <summary>
/// Deterministic feed used in tests and local development: eight fashion notes
/// with stable titles and URLs, recent enough to pass the generator's age
/// filter. It never performs network I/O.
/// </summary>
public sealed class FakeFeedFetcher : IFeedFetcher
{
    public Task<IReadOnlyList<FeedEntry>> FetchAsync(FeedSource source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ct.ThrowIfCancellationRequested();

        // Kept relative to the call so the demo always has a fresh digest. The
        // content and ordering are deterministic.
        var now = DateTimeOffset.UtcNow;
        var sourceName = string.IsNullOrWhiteSpace(source.Name) ? null : source.Name.Trim();

        IReadOnlyList<FeedEntry> entries = DemoFeedNotes.All
            .Select((note, index) => new FeedEntry(
                Title: note.Title,
                Url: note.Url,
                Source: sourceName ?? note.Source,
                PublishedAt: now.AddHours(-(index + 1)),
                Summary: note.Summary,
                ImageUrl: null))
            .ToArray();

        return Task.FromResult(entries);
    }
}
