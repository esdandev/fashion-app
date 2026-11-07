using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FashionApp.Api.Domain;
using FashionApp.Api.Features.Garments;
using FashionApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static TestData;

public sealed class GarmentEndpointTests : IDisposable
{
    private readonly TestAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task List_excludes_archived_and_filters_by_category()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Garments.AddRange(
            NewGarment("Camisa", GarmentCategory.Top, archived: false),
            NewGarment("Jean", GarmentCategory.Bottom, archived: false),
            NewGarment("Viejo", GarmentCategory.Top, archived: true));
        await db.SaveChangesAsync();

        var body = await factory.CreateClient().GetFromJsonAsync<GarmentListResponse>("/api/garments?category=top");
        Assert.Single(body!.Items);
        Assert.Equal("Camisa", body.Items[0].Name);
    }

    [Fact]
    public async Task Delete_archives_instead_of_removing()
    {
        var id = await SeedOneGarment();
        var response = await factory.CreateClient().DeleteAsync($"/api/garments/{id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await db.Garments.FindAsync(id))!.IsArchived);
    }

    [Fact]
    public async Task List_paginates_with_cursor_without_gaps_or_duplicates()
    {
        // Same CreatedAt on purpose: ordering and the cursor must fall back to Id.
        var createdAt = DateTimeOffset.UtcNow.AddDays(-1);
        var garments = Enumerable.Range(0, 30)
            .Select(i =>
            {
                var garment = NewGarment($"Prenda {i}", GarmentCategory.Top);
                garment.CreatedAt = createdAt;
                garment.UpdatedAt = createdAt;
                return garment;
            })
            .ToArray();
        await SeedGarments(factory, garments);

        var client = factory.CreateClient();
        var first = await client.GetFromJsonAsync<GarmentListResponse>("/api/garments");

        Assert.Equal(24, first!.Items.Count);
        Assert.NotNull(first.NextCursor);

        var second = await client.GetFromJsonAsync<GarmentListResponse>(
            $"/api/garments?cursor={first.NextCursor}");

        Assert.NotNull(second);
        Assert.Equal(6, second!.Items.Count);
        Assert.Null(second.NextCursor);
        Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
        Assert.Equal(30, first.Items.Concat(second.Items).Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public async Task List_filters_by_color_and_season_case_insensitively()
    {
        var camisa = NewGarment("Camisa", GarmentCategory.Top);
        camisa.ColorName = "Beige";
        camisa.Seasons = "spring,summer";

        var abrigo = NewGarment("Abrigo", GarmentCategory.Outerwear);
        abrigo.ColorName = "negro";
        abrigo.Seasons = "autumn,winter";

        await SeedGarments(factory, camisa, abrigo);

        var body = await factory.CreateClient()
            .GetFromJsonAsync<GarmentListResponse>("/api/garments?color=BEIGE&season=Summer");

        Assert.Single(body!.Items);
        Assert.Equal("Camisa", body.Items[0].Name);
    }

    [Fact]
    public async Task List_projects_dto_with_media_urls_and_parsed_lists()
    {
        var garment = NewGarment("Camisa", GarmentCategory.Top);
        garment.Seasons = "spring, summer";
        garment.StyleTagsJson = """["minimalista","casual"]""";
        garment.Confidence = 0.9;
        await SeedGarments(factory, garment);

        var body = await factory.CreateClient().GetFromJsonAsync<GarmentListResponse>("/api/garments");
        var item = Assert.Single(body!.Items);

        Assert.Equal(garment.Id, item.Id);
        Assert.Equal("Camisa", item.Name);
        Assert.Equal("top", item.Category);
        Assert.Equal(3, item.Formality);
        Assert.Equal(0.9, item.Confidence);
        Assert.Equal(new[] { "spring", "summer" }, item.Seasons);
        Assert.Equal(new[] { "minimalista", "casual" }, item.StyleTags);
        Assert.Equal($"/media/{garment.ContentHash}.webp", item.ImageUrl);
        Assert.Equal($"/media/{garment.ContentHash}-thumb.webp", item.ThumbUrl);
    }

    [Fact]
    public async Task Patch_updates_fields_and_serializes_lists()
    {
        var id = await SeedOneGarment();
        var request = new PatchGarmentRequest(
            "Blazer", "outerwear", "azul", "#123456", 4,
            ["autumn", "winter"], ["minimalista", "formal"], "Para el trabajo");

        var response = await factory.CreateClient().PatchAsJsonAsync($"/api/garments/{id}", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<GarmentDto>();
        Assert.Equal("Blazer", dto!.Name);
        Assert.Equal("outerwear", dto.Category);
        Assert.Equal("azul", dto.ColorName);
        Assert.Equal("#123456", dto.ColorHex);
        Assert.Equal(4, dto.Formality);
        Assert.Equal(new[] { "autumn", "winter" }, dto.Seasons);
        Assert.Equal(new[] { "minimalista", "formal" }, dto.StyleTags);

        var saved = await GetGarment(id);
        Assert.NotNull(saved);
        Assert.Equal("autumn,winter", saved!.Seasons);
        Assert.Equal("""["minimalista","formal"]""", saved.StyleTagsJson);
        Assert.Equal("Para el trabajo", saved.Notes);
    }

    [Fact]
    public async Task Patch_only_changes_provided_fields()
    {
        var id = await SeedOneGarment();
        var request = new PatchGarmentRequest("Camisa nueva", null, null, null, null, null, null, null);

        var response = await factory.CreateClient().PatchAsJsonAsync($"/api/garments/{id}", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<GarmentDto>();
        Assert.Equal("Camisa nueva", dto!.Name);
        Assert.Equal("top", dto.Category);
        Assert.Equal("beige", dto.ColorName);
        Assert.Equal(3, dto.Formality);
        Assert.Equal(new[] { "spring", "summer" }, dto.Seasons);
    }

    [Theory]
    [InlineData("inventada", 3)]
    [InlineData("top", 0)]
    [InlineData("top", 6)]
    public async Task Patch_rejects_invalid_category_or_formality(string category, int formality)
    {
        var id = await SeedOneGarment();
        var request = new PatchGarmentRequest(null, category, null, null, formality, null, null, null);

        var response = await factory.CreateClient().PatchAsJsonAsync($"/api/garments/{id}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var saved = await GetGarment(id);
        Assert.NotNull(saved);
        Assert.Equal("Camisa de lino", saved!.Name);
        Assert.Equal(3, saved.Formality);
    }

    [Fact]
    public async Task Patch_returns_400_for_unknown_garment()
    {
        var request = new PatchGarmentRequest("Fantasma", null, null, null, null, null, null, null);

        var response = await factory.CreateClient()
            .PatchAsJsonAsync($"/api/garments/{Guid.NewGuid()}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Delete_returns_404_for_unknown_garment()
    {
        var response = await factory.CreateClient().DeleteAsync($"/api/garments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private Task<Guid> SeedOneGarment() => TestData.SeedOneGarment(factory);

    private async Task<Garment?> GetGarment(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Garments.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id);
    }
}
