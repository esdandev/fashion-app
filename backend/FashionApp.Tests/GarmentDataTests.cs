using FashionApp.Api.Domain;
using Microsoft.EntityFrameworkCore;

public sealed class GarmentDataTests
{
    [Fact]
    public async Task Can_persist_and_read_a_garment()
    {
        await using var db = TestDb.Create();
        db.Garments.Add(new Garment
        {
            Id = Guid.NewGuid(),
            Name = "Camisa de lino",
            Category = GarmentCategory.Top,
            ColorName = "beige",
            ColorHex = "#D8C3A5",
            Seasons = "spring,summer",
            Formality = 3,
            ContentHash = "abc",
            ImagePath = "abc.webp",
            ThumbPath = "abc-thumb.webp",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var saved = await db.Garments.AsNoTracking().SingleAsync();
        Assert.Equal("Camisa de lino", saved.Name);
        Assert.Equal(GarmentCategory.Top, saved.Category);
        Assert.False(saved.IsArchived);
    }
}
