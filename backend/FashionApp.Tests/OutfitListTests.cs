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
}
