using System.Net;
using System.Net.Http.Json;
using FashionApp.Api.Domain;
using FashionApp.Api.Features.Outfits;
using FashionApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed class OutfitListTests : IDisposable
{
    private readonly TestAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Lists_saved_outfits_with_their_garments()
    {
        var (outfitId, garmentId) = await SeedOutfitWithGarment(factory, "Look de trabajo");
        var response = await factory.CreateClient().GetAsync("/api/outfits");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var outfits = await response.Content.ReadFromJsonAsync<List<OutfitDto>>();
        var outfit = Assert.Single(outfits!);
        Assert.Equal("Look de trabajo", outfit.Name);
        Assert.Equal(garmentId, Assert.Single(outfit.Items).Garment.Id);
    }

    [Fact]
    public async Task Orders_favorites_first_then_newest_created()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedOutfit(factory, "Viejo favorito", OutfitSource.Manual, now.AddDays(-3), favorite: true);
        await SeedOutfit(factory, "Nuevo normal", OutfitSource.Manual, now.AddDays(-1));
        await SeedOutfit(factory, "Viejo normal", OutfitSource.Manual, now.AddDays(-2));
        await SeedOutfit(factory, "Nuevo favorito", OutfitSource.Ai, now, favorite: true);

        var outfits = await GetOutfits(factory);

        Assert.Equal(
            ["Nuevo favorito", "Viejo favorito", "Nuevo normal", "Viejo normal"],
            outfits.Select(o => o.Name));
    }

    [Fact]
    public async Task Returns_items_ordered_by_sort_order()
    {
        var top = TestData.NewGarment("Camisa de lino", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        var shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);

        await SeedOutfit(
            factory,
            "Look ordenado",
            OutfitSource.Manual,
            DateTimeOffset.UtcNow,
            items:
            [
                (shoes, 0, 2),
                (top, 0, 0),
                (bottom, 0, 1)
            ]);

        var outfit = Assert.Single(await GetOutfits(factory));
        Assert.Equal(
            [top.Id, bottom.Id, shoes.Id],
            outfit.Items.Select(i => i.Garment.Id));
        Assert.Equal([0, 1, 2], outfit.Items.Select(i => i.SortOrder));
    }

    [Fact]
    public async Task Keeps_archived_garments_in_saved_outfits()
    {
        var archived = TestData.NewGarment(
            "Campera archivada", GarmentCategory.Outerwear, archived: true);

        await SeedOutfit(
            factory,
            "Look con archivo",
            OutfitSource.Manual,
            DateTimeOffset.UtcNow,
            items: [(archived, 1, 0)]);

        var outfit = Assert.Single(await GetOutfits(factory));
        Assert.Equal(archived.Id, Assert.Single(outfit.Items).Garment.Id);
    }

    [Fact]
    public async Task Serializes_source_as_lowercase_ai_or_manual()
    {
        await SeedOutfit(
            factory, "Sugerido por IA", OutfitSource.Ai, DateTimeOffset.UtcNow.AddMinutes(-1));
        await SeedOutfit(factory, "Armado a mano", OutfitSource.Manual, DateTimeOffset.UtcNow);

        var outfits = await GetOutfits(factory);

        Assert.Equal("ai", outfits.Single(o => o.Name == "Sugerido por IA").Source);
        Assert.Equal("manual", outfits.Single(o => o.Name == "Armado a mano").Source);
    }

    [Fact]
    public async Task Requires_token()
    {
        using var securedFactory = new TestAppFactory { Token = "secreto" };

        var response = await securedFactory.CreateClient().GetAsync("/api/outfits");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<List<OutfitDto>> GetOutfits(TestAppFactory factory)
    {
        var response = await factory.CreateClient().GetAsync("/api/outfits");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<OutfitDto>>())!;
    }

    private static async Task<(Guid outfitId, Guid garmentId)> SeedOutfitWithGarment(
        TestAppFactory factory,
        string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var garment = TestData.NewGarment("Camisa de lino", GarmentCategory.Top);
        var outfit = new Outfit
        {
            Id = Guid.NewGuid(),
            Name = name,
            Source = OutfitSource.Manual,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        outfit.Items.Add(new OutfitItem
        {
            OutfitId = outfit.Id,
            GarmentId = garment.Id,
            Layer = 0,
            SortOrder = 0,
            Garment = garment
        });

        db.Outfits.Add(outfit);
        await db.SaveChangesAsync();

        return (outfit.Id, garment.Id);
    }

    private static async Task<Guid> SeedOutfit(
        TestAppFactory factory,
        string name,
        OutfitSource source,
        DateTimeOffset? createdAt = null,
        bool favorite = false,
        (Garment Garment, int Layer, int SortOrder)[]? items = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var timestamp = createdAt ?? DateTimeOffset.UtcNow;
        var outfit = new Outfit
        {
            Id = Guid.NewGuid(),
            Name = name,
            Source = source,
            IsFavorite = favorite,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };

        foreach (var (garment, layer, sortOrder) in items ?? [])
        {
            outfit.Items.Add(new OutfitItem
            {
                OutfitId = outfit.Id,
                GarmentId = garment.Id,
                Layer = layer,
                SortOrder = sortOrder,
                Garment = garment
            });
        }

        db.Outfits.Add(outfit);
        await db.SaveChangesAsync();

        return outfit.Id;
    }
}
