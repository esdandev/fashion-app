using System.Net;
using System.Net.Http.Json;
using FashionApp.Api.Domain;
using FashionApp.Api.Features.Outfits;
using FashionApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed class OutfitSuggestEndpointTests : IDisposable
{
    private readonly TestAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Suggest_returns_rule_based_outfits_from_the_active_wardrobe()
    {
        await SeedWardrobeAsync(
            (GarmentCategory.Top, "Camisa", 3),
            (GarmentCategory.Bottom, "Jean", 3),
            (GarmentCategory.Shoes, "Zapatillas", 3));

        var result = await SuggestAsync("trabajo", "templado");

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var body = await result.Content.ReadFromJsonAsync<SuggestOutfitsResponse>();
        Assert.NotEmpty(body!.Outfits);
        Assert.All(body.Outfits, outfit => Assert.NotEmpty(outfit.GarmentIds));
    }

    [Fact]
    public async Task Suggest_returns_no_outfits_when_the_wardrobe_is_incomplete()
    {
        await SeedWardrobeAsync((GarmentCategory.Top, "Camisa", 3));

        var result = await SuggestAsync("trabajo", "templado");

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var body = await result.Content.ReadFromJsonAsync<SuggestOutfitsResponse>();
        Assert.Empty(body!.Outfits);
    }

    [Theory]
    [InlineData("granizo")]
    [InlineData("otoño")]
    public async Task Suggest_rejects_an_unknown_weather_with_400(string weather)
    {
        await SeedWardrobeAsync(
            (GarmentCategory.Top, "Camisa", 3),
            (GarmentCategory.Bottom, "Jean", 3),
            (GarmentCategory.Shoes, "Zapatillas", 3));

        var result = await SuggestAsync("trabajo", weather);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("application/problem+json", result.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Suggest_defaults_to_temperate_when_the_weather_is_missing(string? weather)
    {
        await SeedWardrobeAsync(
            (GarmentCategory.Top, "Camisa", 3),
            (GarmentCategory.Bottom, "Jean", 3),
            (GarmentCategory.Shoes, "Zapatillas", 3));

        var result = await SuggestAsync("trabajo", weather);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact]
    public async Task Suggest_requires_the_token()
    {
        using var privateFactory = new TestAppFactory { Token = "secreto" };
        var client = privateFactory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/outfits/suggest",
            new { occasion = "trabajo", weather = "templado" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Suggest_is_blocked_in_demo()
    {
        using var demoFactory = new TestAppFactory { Demo = true };
        var client = demoFactory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/outfits/suggest",
            new { occasion = "trabajo", weather = "templado" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SuggestAsync(
        string? occasion,
        string? weather)
    {
        var client = factory.CreateClient();
        return await client.PostAsJsonAsync(
            "/api/outfits/suggest",
            new { occasion, weather });
    }

    private async Task SeedWardrobeAsync(
        params (GarmentCategory Category, string Name, int Formality)[] garments)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Garments.AddRange(garments.Select(garment =>
        {
            var entity = TestData.NewGarment(garment.Name, garment.Category);
            entity.Formality = garment.Formality;
            entity.Seasons = "spring,summer,autumn,winter";
            return entity;
        }));
        await db.SaveChangesAsync();
    }
}
