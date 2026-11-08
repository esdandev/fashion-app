using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FashionApp.Api.Features.Digest;
using FashionApp.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DigestEntity = FashionApp.Api.Domain.Digest;

public sealed class DigestRefreshTests
{
    private const string AdminSecretHeader = "X-Admin-Secret";
    private const string AdminSecret = "s3creto";

    private static readonly DateTimeOffset FixedNow =
        new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Refresh_requires_the_admin_secret()
    {
        using var factory = new TestAppFactory { AdminSecret = AdminSecret };
        var client = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsync("/api/digest/refresh", null)).StatusCode);

        client.DefaultRequestHeaders.Add(AdminSecretHeader, "otro");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsync("/api/digest/refresh", null)).StatusCode);

        client.DefaultRequestHeaders.Remove(AdminSecretHeader);
        client.DefaultRequestHeaders.Add(AdminSecretHeader, AdminSecret);
        var ok = await client.PostAsync("/api/digest/refresh", null);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var digest = await ok.Content.ReadFromJsonAsync<DigestDto>();
        Assert.NotNull(digest);
        Assert.NotEmpty(digest!.Items);
    }

    [Fact]
    public async Task Refresh_is_rate_limited_by_a_one_hour_cooldown()
    {
        var guard = new DigestRefreshGuard(
            new ManualTimeProvider(DateTimeOffset.Parse("2026-11-08T10:00:00Z")));

        Assert.True(guard.TryBegin());
        Assert.False(guard.TryBegin());
        Assert.True(guard.Remaining() > TimeSpan.Zero);

        guard.Clock.Advance(TimeSpan.FromHours(1));

        Assert.True(guard.TryBegin());
    }

    [Fact]
    public void Guard_reset_releases_the_cooldown()
    {
        var guard = new DigestRefreshGuard(
            new ManualTimeProvider(DateTimeOffset.Parse("2026-11-08T10:00:00Z")));

        Assert.True(guard.TryBegin());
        Assert.False(guard.TryBegin());

        guard.Reset();

        Assert.True(guard.TryBegin());
    }

    [Fact]
    public async Task Refresh_returns_429_with_retry_after_during_the_cooldown()
    {
        using var factory = new TestAppFactory { AdminSecret = AdminSecret };
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminSecretHeader, AdminSecret);

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsync("/api/digest/refresh", null)).StatusCode);

        var limited = await client.PostAsync("/api/digest/refresh", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.True(limited.Headers.RetryAfter!.Delta > TimeSpan.Zero);
        // Prove this is the cooldown guard, not the per-IP limiter (whose
        // title is "Demasiadas solicitudes").
        var problem = await limited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Refresh en espera", problem.GetProperty("title").GetString());
        Assert.Contains(
            "Ya se generó el digest hace menos de una hora",
            problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Refresh_is_blocked_in_demo_even_with_the_admin_secret()
    {
        using var factory = new TestAppFactory
        {
            AdminSecret = AdminSecret,
            Demo = true
        };
        var client = factory.CreateClient();

        // Demo blocks the POST before the handler inspects the secret: a
        // missing header is 403, not 401.
        var anonymous = await client.PostAsync("/api/digest/refresh", null);
        Assert.Equal(HttpStatusCode.Forbidden, anonymous.StatusCode);

        client.DefaultRequestHeaders.Add(AdminSecretHeader, AdminSecret);
        var response = await client.PostAsync("/api/digest/refresh", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_is_forbidden_when_no_admin_secret_is_configured()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminSecretHeader, "cualquiera");

        var response = await client.PostAsync("/api/digest/refresh", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_returns_502_and_keeps_the_previous_digest_when_generation_returns_null()
    {
        using var baseFactory = new TestAppFactory { AdminSecret = AdminSecret };
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDigestGenerator>();
                services.AddScoped<IDigestGenerator, NullDigestGenerator>();
            }));

        await SeedDigest(factory, "Anterior", new DateOnly(2026, 11, 7));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminSecretHeader, AdminSecret);
        var response = await client.PostAsync("/api/digest/refresh", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "No se pudo generar el digest; se conserva el anterior.",
            problem.GetProperty("detail").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var digest = await db.Digests
            .AsNoTracking()
            .OrderByDescending(row => row.Date)
            .SingleAsync();
        Assert.Equal(new DateOnly(2026, 11, 7), digest.Date);
        Assert.Equal("Anterior", digest.Summary);
    }

    [Fact]
    public async Task Refresh_returns_502_and_releases_the_cooldown_when_generation_throws()
    {
        using var baseFactory = new TestAppFactory { AdminSecret = AdminSecret };
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDigestGenerator>();
                services.AddScoped<IDigestGenerator, ThrowingDigestGenerator>();
            }));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminSecretHeader, AdminSecret);

        var first = await client.PostAsync("/api/digest/refresh", null);
        Assert.Equal(HttpStatusCode.BadGateway, first.StatusCode);
        var problem = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Digest no disponible", problem.GetProperty("title").GetString());

        // The failed run must not lock the admin out: the second attempt hits
        // the generator again instead of returning 429.
        var second = await client.PostAsync("/api/digest/refresh", null);
        Assert.Equal(HttpStatusCode.BadGateway, second.StatusCode);
    }

    [Fact]
    public async Task Refresh_only_includes_related_garments_when_the_app_token_is_valid()
    {
        using var anonymousFactory = new TestAppFactory
        {
            AdminSecret = AdminSecret,
            Token = "app-token"
        };
        await SeedGarmentWithTags(anonymousFactory, "Camisa lino", ["minimalista"]);
        var anonymousClient = anonymousFactory.CreateClient();
        anonymousClient.DefaultRequestHeaders.Add(AdminSecretHeader, AdminSecret);

        var anonymous = await anonymousClient.PostAsync("/api/digest/refresh", null);
        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
        var anonymousDigest = await anonymous.Content.ReadFromJsonAsync<DigestDto>();
        Assert.All(
            anonymousDigest!.Items,
            item => Assert.Empty(item.RelatedGarments));

        using var authorizedFactory = new TestAppFactory
        {
            AdminSecret = AdminSecret,
            Token = "app-token"
        };
        await SeedGarmentWithTags(authorizedFactory, "Camisa lino", ["minimalista"]);
        var authorizedClient = authorizedFactory.CreateClient();
        authorizedClient.DefaultRequestHeaders.Add(AdminSecretHeader, AdminSecret);
        authorizedClient.DefaultRequestHeaders.Add("X-App-Token", "app-token");

        var authorized = await authorizedClient.PostAsync("/api/digest/refresh", null);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        var authorizedDigest = await authorized.Content.ReadFromJsonAsync<DigestDto>();
        Assert.Single(authorizedDigest!.Items[0].RelatedGarments);
    }

    private static async Task SeedDigest(
        WebApplicationFactory<Program> factory,
        string summary,
        DateOnly date)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Digests.Add(new DigestEntity
        {
            Id = Guid.NewGuid(),
            Date = date,
            Summary = summary,
            GeneratedAt = FixedNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedGarmentWithTags(
        TestAppFactory factory,
        string name,
        string[] tags)
    {
        var hash = Guid.NewGuid().ToString("N");
        await TestData.SeedGarments(
            factory,
            new FashionApp.Api.Domain.Garment
            {
                Id = Guid.NewGuid(),
                Name = name,
                Category = FashionApp.Api.Domain.GarmentCategory.Top,
                ColorName = "beige",
                ColorHex = "#D8C3A5",
                Seasons = "spring,summer",
                Formality = 3,
                StyleTagsJson = JsonSerializer.Serialize(tags),
                ContentHash = hash,
                ImagePath = $"{hash}.webp",
                ThumbPath = $"{hash}-thumb.webp",
                CreatedAt = FixedNow,
                UpdatedAt = FixedNow
            });
    }

    private sealed class NullDigestGenerator : IDigestGenerator
    {
        public Task<DigestEntity?> GenerateAsync(CancellationToken ct) =>
            Task.FromResult<DigestEntity?>(null);
    }

    private sealed class ThrowingDigestGenerator : IDigestGenerator
    {
        public Task<DigestEntity?> GenerateAsync(CancellationToken ct) =>
            throw new InvalidOperationException("El proveedor de resumen no está disponible.");
    }
}
