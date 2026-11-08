using FashionApp.Api.Domain;
using FashionApp.Api.Features.Digest;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Ai;
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

    private DigestGenerator Build(
        IReadOnlyList<FeedEntry> feeds,
        int drafts,
        string? summary = null,
        int itemSummaryLength = 0,
        string environmentName = "Testing")
    {
        var values = new Dictionary<string, string?>
        {
            ["Digest:MaxItems"] = "10",
            ["Digest:MaxAgeHours"] = "48",
        };

        if (feeds.Count > 0)
        {
            values["Digest:Feeds:0:Name"] = "Vogue";
            values["Digest:Feeds:0:Url"] = "https://feed.test/rss";
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new DigestGenerator(
            new StubFeedFetcher(feeds),
            new StubSummarizer(drafts, summary, itemSummaryLength),
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

    private sealed class StubSummarizer(
        int maxDrafts,
        string? summary,
        int itemSummaryLength) : IDigestSummarizer
    {
        public Task<DigestSummary> SummarizeAsync(
            IReadOnlyList<FeedEntry> entries,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

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

            return Task.FromResult(new DigestSummary(summary ?? "Resumen de prueba", drafts));
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
