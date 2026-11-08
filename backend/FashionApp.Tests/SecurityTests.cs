using System.Net;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Demo;
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
    }

    [Fact]
    public async Task Demo_seed_is_idempotent()
    {
        using var factory = new TestAppFactory { Demo = true };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var first = await db.Garments.CountAsync();
        await DemoSeeder.SeedAsync(db);
        Assert.Equal(first, await db.Garments.CountAsync());
        Assert.InRange(first, 12, 15);
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
    }
}
