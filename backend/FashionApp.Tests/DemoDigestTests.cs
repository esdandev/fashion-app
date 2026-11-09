using System.Net;
using System.Net.Http.Json;
using FashionApp.Api.Domain;
using FashionApp.Api.Features.Digest;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Demo;
using FashionApp.Api.Infrastructure.Feeds;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

public sealed class DemoDigestTests
{
    [Fact]
    public async Task Demo_serves_a_seeded_digest_without_calling_feeds_or_ai()
    {
        var fetcher = new CountingFeedFetcher();
        var summarizer = new CountingSummarizer();

        using var baseFactory = new TestAppFactory { Demo = true };
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFeedFetcher>();
                services.AddSingleton<IFeedFetcher>(fetcher);
                services.RemoveAll<IDigestSummarizer>();
                services.AddSingleton<IDigestSummarizer>(summarizer);
            }));

        var response = await factory.CreateClient().GetAsync("/api/digest/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var digest = await response.Content.ReadFromJsonAsync<DigestDto>();
        Assert.NotNull(digest);
        Assert.InRange(digest!.Items.Count, 6, 8);
        Assert.False(string.IsNullOrWhiteSpace(digest.Summary));
        Assert.All(digest.Items, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.False(string.IsNullOrWhiteSpace(item.Summary));
            Assert.StartsWith("https://", item.Url);
        });

        // The demo digest is seeded data, not a background generation.
        Assert.Equal(0, fetcher.Calls);
        Assert.Equal(0, summarizer.Calls);
        Assert.DoesNotContain(
            factory.Services.GetServices<IHostedService>(),
            service => service is DigestBackgroundService);
    }

    [Fact]
    public async Task Demo_digest_seed_is_idempotent()
    {
        using var factory = new TestAppFactory { Demo = true };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var firstDigests = await db.Digests.CountAsync();
        var firstItems = await db.DigestItems.CountAsync();

        await DemoSeeder.SeedAsync(db, factory.UploadsPath);

        Assert.Equal(1, firstDigests);
        Assert.InRange(firstItems, 6, 8);
        Assert.Equal(firstDigests, await db.Digests.CountAsync());
        Assert.Equal(firstItems, await db.DigestItems.CountAsync());
    }

    [Fact]
    public async Task Digest_seed_runs_even_when_the_wardrobe_already_has_rows()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "fashion-tests",
            Guid.NewGuid().ToString("N"));
        var uploads = Path.Combine(root, "uploads");
        Directory.CreateDirectory(uploads);

        await using var db = TestDb.Create(Path.Combine(root, "demo.db"));
        db.Garments.Add(TestData.NewGarment("Prenda ajena", GarmentCategory.Top));
        await db.SaveChangesAsync();

        await DemoSeeder.SeedAsync(db, uploads);

        // The garment guard kept the existing wardrobe untouched, but the
        // digest still exists because it has its own empty-table guard.
        Assert.Equal(1, await db.Garments.CountAsync());
        var digest = Assert.Single(await db.Digests.ToListAsync());
        Assert.InRange(await db.DigestItems.CountAsync(item => item.DigestId == digest.Id), 6, 8);
    }

    [Fact]
    public async Task Demo_seed_repairs_legacy_example_com_links()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "fashion-tests",
            Guid.NewGuid().ToString("N"));
        var uploads = Path.Combine(root, "uploads");
        Directory.CreateDirectory(uploads);

        await using var db = TestDb.Create(Path.Combine(root, "demo.db"));
        var now = DateTimeOffset.UtcNow;
        var digest = new Digest
        {
            Id = Guid.NewGuid(),
            Date = DateOnly.FromDateTime(now.UtcDateTime),
            Summary = "Digest viejo",
            GeneratedAt = now
        };
        digest.Items.Add(new DigestItem
        {
            Id = Guid.NewGuid(),
            DigestId = digest.Id,
            Title = DemoFeedNotes.All[0].Title,
            Url = "https://example.com/nota-1",
            Source = "Vogue",
            Summary = "Resumen viejo.",
            StyleTagsJson = "[]",
            PublishedAt = now,
            SortOrder = 0
        });
        db.Digests.Add(digest);
        await db.SaveChangesAsync();

        await DemoSeeder.SeedAsync(db, uploads);

        var item = Assert.Single(await db.DigestItems.ToListAsync());
        Assert.Equal(DemoFeedNotes.All[0].Url, item.Url);
        Assert.DoesNotContain("example.com", item.Url);
    }

    private sealed class CountingFeedFetcher : IFeedFetcher
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<FeedEntry>> FetchAsync(FeedSource source, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<FeedEntry>>([]);
        }
    }

    private sealed class CountingSummarizer : IDigestSummarizer
    {
        public int Calls { get; private set; }

        public Task<DigestSummary> SummarizeAsync(
            IReadOnlyList<FeedEntry> entries,
            CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new DigestSummary(null, []));
        }
    }
}
