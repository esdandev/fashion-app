using FashionApp.Api.Domain;
using FashionApp.Api.Features.Digest;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Feeds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class DigestGeneratorTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        "fashion-tests",
        Guid.NewGuid().ToString("N"),
        "digest-generator.db");

    private readonly List<AppDbContext> _contexts = [];

    public void Dispose()
    {
        foreach (var context in _contexts)
            context.Dispose();

        // Deliberately do not call SqliteConnection.ClearAllPools(): it is a
        // process-wide side effect and xUnit runs other test classes in
        // parallel. The temporary database directory is left to the OS.
    }

    [Fact]
    public async Task Generation_dedupes_and_persists_one_digest_per_date()
    {
        var generator = Build(feeds: [
            Entry("Misma nota", "https://example.com/a"),
            Entry("Misma nota", "https://example.com/a"),
            Entry("Otra", "https://example.com/b"),
        ], drafts: 2);

        var digest = await generator.GenerateAsync(default);
        Assert.NotNull(digest);
        Assert.Equal(2, digest!.Items.Count);
        Assert.Equal(new DateOnly(2026, 11, 8), digest.Date);

        var again = await generator.GenerateAsync(default);
        Assert.NotNull(again);
        await using var db = NewDb();
        Assert.Single(await db.Digests.ToListAsync());
        Assert.Equal(2, await db.DigestItems.CountAsync());
    }

    [Fact]
    public async Task Empty_result_keeps_the_previous_digest()
    {
        await SeedExistingDigest();
        var generator = Build(feeds: [], drafts: 0);

        Assert.Null(await generator.GenerateAsync(default));

        await using var db = NewDb();
        Assert.Single(await db.Digests.ToListAsync());
    }

    [Fact]
    public async Task No_valid_items_keep_the_previous_digest()
    {
        await SeedExistingDigest();
        var generator = Build(feeds: [Entry("Nota", "https://example.com/a")], drafts: 0);

        Assert.Null(await generator.GenerateAsync(default));

        await using var db = NewDb();
        var digest = Assert.Single(await db.Digests.ToListAsync());
        Assert.Equal("Anterior", digest.Summary);
    }

    [Fact]
    public async Task Generation_drops_entries_older_than_the_max_age()
    {
        var generator = Build(feeds: [
            Entry("Reciente", "https://example.com/fresh", Now.AddHours(-1)),
            Entry("Vieja", "https://example.com/old", Now.AddHours(-49)),
            Entry("Sin fecha", "https://example.com/undated"),
        ], drafts: 10);

        var digest = await generator.GenerateAsync(default);

        Assert.NotNull(digest);
        Assert.Equal(
            ["Reciente", "Sin fecha"],
            digest!.Items.Select(item => item.Title));
    }

    [Fact]
    public async Task Generation_dedupes_by_normalized_title()
    {
        var generator = Build(feeds: [
            Entry("Misma Nota", "https://example.com/a"),
            Entry("  misma   NOTA ", "https://example.com/b"),
        ], drafts: 10);

        var digest = await generator.GenerateAsync(default);

        Assert.NotNull(digest);
        Assert.Single(digest!.Items);
    }

    [Fact]
    public async Task Regenerating_the_same_day_replaces_the_items_without_duplicates()
    {
        var generator = Build(feeds: [
            Entry("Primera", "https://example.com/a"),
            Entry("Segunda", "https://example.com/b"),
        ], drafts: 2);

        var first = await generator.GenerateAsync(default);
        Assert.NotNull(first);
        var firstIds = first!.Items.Select(item => item.Id).ToList();

        var second = await generator.GenerateAsync(default);

        Assert.NotNull(second);
        Assert.Equal(first.Id, second!.Id);
        Assert.DoesNotContain(second.Items, item => firstIds.Contains(item.Id));

        await using var db = NewDb();
        Assert.Single(await db.Digests.ToListAsync());
        Assert.Equal(2, await db.DigestItems.CountAsync());
    }

    [Fact]
    public async Task Generated_summary_and_items_respect_the_600_char_column_bound()
    {
        var generator = Build(
            feeds: [Entry("Nota", "https://example.com/a")],
            drafts: 1,
            summary: new string('x', 900),
            itemSummaryLength: 900);

        var digest = await generator.GenerateAsync(default);

        Assert.NotNull(digest);
        Assert.Equal(600, digest!.Summary.Length);
        Assert.Equal(600, Assert.Single(digest.Items).Summary.Length);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_persists_nothing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var generator = Build(
            feeds: [Entry("Nota", "https://example.com/a")],
            drafts: 1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => generator.GenerateAsync(cts.Token));

        await using var db = NewDb();
        Assert.Empty(await db.Digests.ToListAsync());
        Assert.Empty(await db.DigestItems.ToListAsync());
    }

    [Fact]
    public async Task A_null_summary_keeps_the_previous_digest_summary()
    {
        await SeedExistingDigest();
        var generator = Build(
            feeds: [Entry("Nota nueva", "https://example.com/nueva")],
            drafts: 1,
            nullSummary: true);

        var digest = await generator.GenerateAsync(default);

        Assert.NotNull(digest);
        Assert.Equal("Anterior", digest!.Summary);

        await using var db = NewDb();
        Assert.Equal("Anterior", (await db.Digests.SingleAsync()).Summary);
    }

    [Fact]
    public async Task A_null_summary_on_a_new_digest_uses_a_generic_intro()
    {
        var generator = Build(
            feeds: [Entry("Nota nueva", "https://example.com/nueva")],
            drafts: 1,
            nullSummary: true);

        var digest = await generator.GenerateAsync(default);

        Assert.NotNull(digest);
        Assert.Equal(DigestGenerator.DefaultSummary, digest!.Summary);
        Assert.False(string.IsNullOrWhiteSpace(digest.Summary));
    }

    [Fact]
    public async Task A_broken_feed_does_not_discard_the_healthy_feed()
    {
        var healthy = new FeedSource("Sana", "https://feed.test/healthy");
        var broken = new FeedSource("Rota", "https://feed.test/broken");
        var generator = Build(
            feeds: [],
            drafts: 10,
            sources: [healthy, broken],
            fetcher: new PerSourceFeedFetcher(
                broken.Url,
                healthyEntries: [Entry("Nota sana", "https://example.com/sana")],
                healthyUrl: healthy.Url));

        var digest = await generator.GenerateAsync(default);

        Assert.NotNull(digest);
        var item = Assert.Single(digest!.Items);
        Assert.Equal("Nota sana", item.Title);
        Assert.Equal("https://example.com/sana", item.Url);
    }

    [Fact]
    public async Task The_summarizer_never_receives_more_than_twenty_entries()
    {
        var feeds = Enumerable.Range(0, 25)
            .Select(index => Entry(
                $"Nota {index}",
                $"https://example.com/{index}",
                Now.AddMinutes(-index)))
            .ToArray();
        var summarizer = new StubSummarizer(maxDrafts: 10);
        var generator = Build(feeds: feeds, drafts: 10, summarizer: summarizer);

        var digest = await generator.GenerateAsync(default);

        Assert.NotNull(digest);
        Assert.Equal(20, summarizer.ReceivedCount);
        Assert.Equal(10, digest!.Items.Count);
    }

    private DigestGenerator Build(
        IReadOnlyList<FeedEntry> feeds,
        int drafts,
        string? summary = null,
        int itemSummaryLength = 0,
        string environmentName = "Testing",
        bool nullSummary = false,
        IReadOnlyList<FeedSource>? sources = null,
        IFeedFetcher? fetcher = null,
        StubSummarizer? summarizer = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Digest:MaxItems"] = "10",
            ["Digest:MaxAgeHours"] = "48",
        };

        var configuredSources = sources
            ?? (feeds.Count > 0
                ? [new FeedSource("Vogue", "https://feed.test/rss")]
                : []);

        for (var index = 0; index < configuredSources.Count; index++)
        {
            values[$"Digest:Feeds:{index}:Name"] = configuredSources[index].Name;
            values[$"Digest:Feeds:{index}:Url"] = configuredSources[index].Url;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new DigestGenerator(
            fetcher ?? new StubFeedFetcher(feeds),
            summarizer ?? new StubSummarizer(drafts, summary, itemSummaryLength, nullSummary),
            NewDb(),
            configuration,
            new StubEnvironment(environmentName),
            new FixedTimeProvider(Now),
            NullLogger<DigestGenerator>.Instance);
    }

    private AppDbContext NewDb()
    {
        var db = TestDb.Create(_dbPath);
        _contexts.Add(db);
        return db;
    }

    private async Task SeedExistingDigest()
    {
        await using var db = NewDb();
        db.Digests.Add(new Digest
        {
            Id = Guid.NewGuid(),
            Date = DateOnly.FromDateTime(Now.UtcDateTime),
            Summary = "Anterior",
            GeneratedAt = Now.AddDays(-1),
            Items =
            [
                new DigestItem
                {
                    Id = Guid.NewGuid(),
                    Title = "Nota anterior",
                    Url = "https://example.com/anterior",
                    Source = "Vogue",
                    Summary = "Resumen anterior.",
                    StyleTagsJson = """["casual"]""",
                    PublishedAt = Now.AddDays(-1),
                    SortOrder = 0
                }
            ]
        });
        await db.SaveChangesAsync();
    }

    private static FeedEntry Entry(
        string title,
        string url,
        DateTimeOffset? publishedAt = null,
        string? summary = null) =>
        new(title, url, "Vogue", publishedAt, summary, null);

    private sealed class StubFeedFetcher(IReadOnlyList<FeedEntry> entries) : IFeedFetcher
    {
        public Task<IReadOnlyList<FeedEntry>> FetchAsync(FeedSource source, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(entries);
        }
    }

    private sealed class PerSourceFeedFetcher(
        string brokenUrl,
        IReadOnlyList<FeedEntry> healthyEntries,
        string healthyUrl) : IFeedFetcher
    {
        public Task<IReadOnlyList<FeedEntry>> FetchAsync(FeedSource source, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (string.Equals(source.Url, brokenUrl, StringComparison.Ordinal))
                throw new HttpRequestException("El feed no está disponible.");

            return Task.FromResult(string.Equals(source.Url, healthyUrl, StringComparison.Ordinal)
                ? healthyEntries
                : (IReadOnlyList<FeedEntry>)[]);
        }
    }

    private sealed class StubSummarizer(
        int maxDrafts,
        string? summary = null,
        int itemSummaryLength = 0,
        bool nullSummary = false) : IDigestSummarizer
    {
        public int ReceivedCount { get; private set; }

        public Task<DigestSummary> SummarizeAsync(
            IReadOnlyList<FeedEntry> entries,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ReceivedCount = entries.Count;

            var drafts = entries
                .Take(maxDrafts)
                .Select(entry => new DigestDraft(
                    Title: entry.Title,
                    Url: entry.Url,
                    Source: entry.Source,
                    Summary: itemSummaryLength > 0
                        ? new string('s', itemSummaryLength)
                        : entry.Summary ?? "Resumen",
                    StyleTags: ["casual"],
                    ImageUrl: entry.ImageUrl,
                    PublishedAt: entry.PublishedAt))
                .ToList();

            return Task.FromResult(new DigestSummary(
                nullSummary ? null : summary ?? "Resumen de prueba",
                drafts));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "FashionApp.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
