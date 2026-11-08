using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FashionApp.Api.Domain;
using FashionApp.Api.Features.Digest;
using FashionApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed class DigestReadTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    private readonly TestAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Today_returns_digest_with_items_and_related_garments()
    {
        await SeedDigest(factory, tags: ["minimalista"]);
        await SeedGarmentWithTags(factory, "Camisa lino", ["minimalista"]);

        var response = await factory.CreateClient().GetAsync("/api/digest/today");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var digest = await response.Content.ReadFromJsonAsync<DigestDto>();
        var item = Assert.Single(digest!.Items);
        Assert.Equal("https://example.com/nota", item.Url);
        Assert.Equal("Camisa lino", Assert.Single(item.RelatedGarments).Name);
    }

    [Fact]
    public async Task Today_omits_related_garments_without_token_on_a_private_instance()
    {
        using var privateFactory = new TestAppFactory { Token = "secreto" };
        await SeedDigest(privateFactory, tags: ["minimalista"]);
        await SeedGarmentWithTags(privateFactory, "Camisa lino", ["minimalista"]);

        var anonymous = await privateFactory.CreateClient().GetFromJsonAsync<DigestDto>("/api/digest/today");
        Assert.Empty(anonymous!.Items[0].RelatedGarments);

        var client = privateFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-App-Token", "secreto");
        var authorized = await client.GetFromJsonAsync<DigestDto>("/api/digest/today");
        Assert.Single(authorized!.Items[0].RelatedGarments);
    }

    [Fact]
    public async Task Today_supports_etag_and_304()
    {
        await SeedDigest(factory, tags: ["casual"]);
        var client = factory.CreateClient();
        var first = await client.GetAsync("/api/digest/today");
        var etag = first.Headers.ETag!.Tag;
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/digest/today");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));
        var second = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task Today_returns_404_when_no_digest_exists()
    {
        var response = await factory.CreateClient().GetAsync("/api/digest/today");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Today_never_allows_public_caching()
    {
        await SeedDigest(factory, tags: ["casual"]);

        var response = await factory.CreateClient().GetAsync("/api/digest/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.Equal(TimeSpan.FromMinutes(5), response.Headers.CacheControl.MaxAge);
        Assert.Contains("X-App-Token", response.Headers.Vary);
    }

    [Fact]
    public async Task Today_returns_the_most_recent_digest()
    {
        await SeedDigest(factory, tags: ["casual"], date: new DateOnly(2026, 11, 6), summary: "Viejo");
        await SeedDigest(factory, tags: ["casual"], date: new DateOnly(2026, 11, 8), summary: "Reciente");

        var digest = await factory.CreateClient().GetFromJsonAsync<DigestDto>("/api/digest/today");

        Assert.Equal(new DateOnly(2026, 11, 8), digest!.Date);
        Assert.Equal("Reciente", digest.Summary);
    }

    [Fact]
    public async Task Today_caps_related_garments_at_three_and_skips_archived()
    {
        await SeedDigest(factory, tags: ["minimalista"]);
        await SeedGarmentWithTags(factory, "Camisa lino", ["minimalista"]);
        await SeedGarmentWithTags(factory, "Pantalón lino", ["minimalista"]);
        await SeedGarmentWithTags(factory, "Blazer lino", ["minimalista"]);
        await SeedGarmentWithTags(factory, "Falda lino", ["minimalista"]);
        var archived = NewGarmentWithTags("Vestido archivado", ["minimalista"]);
        archived.IsArchived = true;
        await TestData.SeedGarments(factory, archived);

        var digest = await factory.CreateClient().GetFromJsonAsync<DigestDto>("/api/digest/today");

        var related = digest!.Items[0].RelatedGarments;
        Assert.Equal(3, related.Count);
        Assert.DoesNotContain(related, garment => garment.Name == "Vestido archivado");
    }

    [Fact]
    public void Match_orders_by_shared_tags_and_returns_at_most_three()
    {
        var tags = new[] { "minimalista", "casual" };
        var candidates = new[]
        {
            NewGarmentWithTags("Sin match", ["formal"]),
            NewGarmentWithTags("Un match", ["casual"]),
            NewGarmentWithTags("Dos matches", ["casual", "minimalista"]),
            NewGarmentWithTags("Otro uno", ["minimalista"]),
            NewGarmentWithTags("Otro dos", ["minimalista", "casual", "playa"])
        };

        var result = DigestMatching.Match(candidates, tags);

        Assert.Equal(["Dos matches", "Otro dos", "Otro uno"], result.Select(g => g.Name));
    }

    [Fact]
    public void Match_returns_empty_without_tags_or_candidates()
    {
        Assert.Empty(DigestMatching.Match([], ["casual"]));
        Assert.Empty(DigestMatching.Match([NewGarmentWithTags("Camisa", ["casual"])], []));
    }

    private static async Task SeedDigest(
        TestAppFactory factory,
        string[] tags,
        DateOnly? date = null,
        string summary = "Resumen de prueba")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var digest = new Digest
        {
            Id = Guid.NewGuid(),
            Date = date ?? DateOnly.FromDateTime(FixedNow.UtcDateTime),
            Summary = summary,
            GeneratedAt = FixedNow,
            Items =
            [
                new DigestItem
                {
                    Id = Guid.NewGuid(),
                    Title = "Nota de moda",
                    Url = "https://example.com/nota",
                    Source = "Vogue",
                    Summary = "Una nota de prueba.",
                    StyleTagsJson = JsonSerializer.Serialize(tags),
                    ImageUrl = null,
                    PublishedAt = FixedNow.AddHours(-2),
                    SortOrder = 0
                }
            ]
        };

        db.Digests.Add(digest);
        await db.SaveChangesAsync();
    }

    private static async Task SeedGarmentWithTags(
        TestAppFactory factory,
        string name,
        string[] tags)
    {
        var garment = NewGarmentWithTags(name, tags);
        await TestData.SeedGarments(factory, garment);
    }

    private static Garment NewGarmentWithTags(string name, string[] tags)
    {
        var hash = Guid.NewGuid().ToString("N");
        return new Garment
        {
            Id = Guid.NewGuid(),
            Name = name,
            Category = GarmentCategory.Top,
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
        };
    }
}
