using System.Collections.Concurrent;
using System.Net;
using System.Text;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Demo;
using FashionApp.Api.Infrastructure.Quotas;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public sealed class SecurityTests
{
    [Fact]
    public async Task Personal_instance_requires_token()
    {
        using var factory = new TestAppFactory { Token = "secreto", Demo = false };
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/garments")).StatusCode);

        using (var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/garments"))
        {
            wrong.Headers.Add("X-App-Token", "incorrecto");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(wrong)).StatusCode);
        }

        client.DefaultRequestHeaders.Add("X-App-Token", "secreto");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/garments")).StatusCode);
    }

    [Fact]
    public async Task Demo_instance_blocks_writes_but_allows_reads()
    {
        using var factory = new TestAppFactory { Demo = true };
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/garments")).StatusCode);

        using var content = TestImages.Multipart("a.jpg", TestImages.CreateJpeg(200, 200));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/garments", content)).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await db.Garments.Select(g => g.Id).FirstAsync();

        using var patch = new StringContent(
            """{"name":"Cambio bloqueado"}""", Encoding.UTF8, "application/json");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PatchAsync($"/api/garments/{id}", patch)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.DeleteAsync($"/api/garments/{id}")).StatusCode);
    }

    [Fact]
    public async Task Demo_write_guard_wins_over_token_filter()
    {
        // The explicit ruling: demo writes must be 403 even when App:Token is set
        // and the request carries no token (the guard runs before the token filter).
        using var factory = new TestAppFactory { Token = "secreto", Demo = true };
        var client = factory.CreateClient();

        using var content = TestImages.Multipart("a.jpg", TestImages.CreateJpeg(200, 200));
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsync("/api/garments", content)).StatusCode);
    }

    [Fact]
    public async Task Demo_seed_is_idempotent_and_serves_assets()
    {
        using var factory = new TestAppFactory { Demo = true };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var first = await db.Garments.CountAsync();
        await DemoSeeder.SeedAsync(db);
        Assert.Equal(first, await db.Garments.CountAsync());
        Assert.InRange(first, 12, 15);

        var garment = await db.Garments.AsNoTracking().FirstAsync();
        var imagePath = Path.Combine(factory.UploadsPath, garment.ImagePath);
        var thumbPath = Path.Combine(factory.UploadsPath, garment.ThumbPath);
        Assert.True(File.Exists(imagePath), $"Falta el archivo sembrado '{imagePath}'.");
        Assert.True(File.Exists(thumbPath), $"Falta la miniatura sembrada '{thumbPath}'.");

        var client = factory.CreateClient();
        var media = await client.GetAsync($"/media/{garment.ImagePath}");
        Assert.Equal(HttpStatusCode.OK, media.StatusCode);
        Assert.Equal("image/webp", media.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Analysis_quota_returns_429_when_exhausted()
    {
        using var factory = new TestAppFactory();
        using var limited = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                    { ["Ai:DailyAnalysisLimit"] = "1" })));
        var client = limited.CreateClient();

        var first = await client.PostAsync("/api/garments",
            TestImages.Multipart("a.jpg", TestImages.CreateJpeg(600, 600)));
        var second = await client.PostAsync("/api/garments",
            TestImages.Multipart("b.jpg", TestImages.CreateJpeg(601, 601)));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);

        Assert.True(
            second.Headers.TryGetValues("Retry-After", out var values),
            "La respuesta 429 no incluye Retry-After.");
        var retryAfter = Assert.Single(values);
        Assert.True(int.TryParse(retryAfter, out var seconds), "Retry-After no es un entero.");
        Assert.InRange(seconds, 1, 86_400);
    }

    [Fact]
    public async Task Quota_concurrency_never_exceeds_limit()
    {
        var dbPath = Path.Combine(
            Path.GetTempPath(), "fashion-tests", Guid.NewGuid().ToString("N"), "quota.db");
        await using (var setup = TestDb.Create(dbPath))
        {
            Assert.Equal(0, await setup.UsageCounters.CountAsync());
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
                { ["Ai:DailyAnalysisLimit"] = "3" })
            .Build();

        const int attempts = 12;
        var results = new ConcurrentBag<bool>();

        var tasks = Enumerable.Range(0, attempts).Select(async _ =>
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            await using var db = new AppDbContext(options);
            var quota = new AiQuotaService(db, configuration);
            results.Add(await quota.TryConsumeAsync(AiQuotaKinds.Analysis, CancellationToken.None));
        });

        // A SQLITE_BUSY escaping here would fail Task.WhenAll, which is exactly the
        // 500 regression this test guards against.
        await Task.WhenAll(tasks);

        Assert.Equal(3, results.Count(consumed => consumed));

        await using var verify = TestDb.Create(dbPath);
        var counter = await verify.UsageCounters.SingleAsync();
        Assert.Equal(3, counter.Count);
    }
}
