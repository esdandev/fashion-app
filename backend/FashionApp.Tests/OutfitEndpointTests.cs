using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FashionApp.Api.Domain;
using FashionApp.Api.Features.Outfits;
using FashionApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed class OutfitEndpointTests : IDisposable
{
    private readonly TestAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Create_persists_manual_outfit_with_layers_and_input_order()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        var shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);
        var outerwear = TestData.NewGarment("Campera", GarmentCategory.Outerwear);
        var bag = TestData.NewGarment("Cartera", GarmentCategory.Bag);
        var accessory = TestData.NewGarment("Bufanda", GarmentCategory.Accessory);
        await TestData.SeedGarments(factory, top, bottom, shoes, outerwear, bag, accessory);

        var garmentIds = new[]
        {
            shoes.Id, outerwear.Id, top.Id, bag.Id, bottom.Id, accessory.Id
        };
        var request = new CreateOutfitRequest("  Look de oficina  ", "  trabajo  ", garmentIds);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/outfits", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var dto = await response.Content.ReadFromJsonAsync<OutfitDto>();
        Assert.NotNull(dto);
        Assert.NotEqual(Guid.Empty, dto!.Id);
        Assert.Equal("Look de oficina", dto.Name);
        Assert.Equal("trabajo", dto.Occasion);
        Assert.Equal("manual", dto.Source);
        Assert.Null(dto.Rationale);
        Assert.False(dto.IsFavorite);
        Assert.Equal(garmentIds, dto.Items.Select(i => i.Garment.Id));
        Assert.Equal([0, 1, 2, 3, 4, 5], dto.Items.Select(i => i.SortOrder));
        // Bases (including shoes) layer 0, outerwear 1, bags and accessories 2.
        Assert.Equal([0, 1, 0, 2, 0, 2], dto.Items.Select(i => i.Layer));
        Assert.Equal($"/api/outfits/{dto.Id}", response.Headers.Location?.ToString());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Outfits
            .Include(o => o.Items)
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(dto.Id, saved.Id);
        Assert.Equal(OutfitSource.Manual, saved.Source);
        Assert.Equal(6, saved.Items.Count);
        Assert.Equal(6, await db.Garments.CountAsync());
    }

    [Fact]
    public async Task Create_accepts_ai_source_and_rationale_for_a_saved_proposal()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        var shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);
        await TestData.SeedGarments(factory, top, bottom, shoes);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/outfits",
            new CreateOutfitRequest(
                "Propuesta de dos piezas",
                "trabajo",
                [top.Id, bottom.Id, shoes.Id],
                "ai",
                "  Ideal para trabajar con clima templado.  "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<OutfitDto>();
        Assert.NotNull(dto);
        Assert.Equal("ai", dto!.Source);
        Assert.Equal("Ideal para trabajar con clima templado.", dto.Rationale);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Outfits.AsNoTracking().SingleAsync();
        Assert.Equal(OutfitSource.Ai, saved.Source);
        Assert.Equal("Ideal para trabajar con clima templado.", saved.Rationale);
    }

    [Fact]
    public async Task Create_rejects_an_unknown_source()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        var shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);
        await TestData.SeedGarments(factory, top, bottom, shoes);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/outfits",
            new CreateOutfitRequest(
                "Look", null, [top.Id, bottom.Id, shoes.Id], "otra"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("La fuente debe ser 'ai' o 'manual'.", await GetProblemDetailAsync(response));
    }

    [Fact]
    public async Task Create_rejects_invalid_compositions_with_400_and_does_not_persist()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        var shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);
        await TestData.SeedGarments(factory, top, bottom, shoes);
        var client = factory.CreateClient();

        var cases = new (Guid[] GarmentIds, string Detail)[]
        {
            ([top.Id, bottom.Id], "El outfit necesita calzado."),
            ([top.Id, shoes.Id], "El outfit necesita una parte superior y una inferior, o un vestido."),
            ([top.Id, top.Id, bottom.Id, shoes.Id], "No se puede repetir la misma prenda."),
            ([], "El outfit necesita una parte superior y una inferior, o un vestido.")
        };

        foreach (var (garmentIds, detail) in cases)
        {
            var response = await client.PostAsJsonAsync(
                "/api/outfits", new CreateOutfitRequest("Look inválido", null, garmentIds));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(detail, await GetProblemDetailAsync(response));
        }

        Assert.Equal(0, await CountAsync(db => db.Outfits.CountAsync()));
    }

    [Fact]
    public async Task Create_rejects_name_outside_length_bounds()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        var shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);
        await TestData.SeedGarments(factory, top, bottom, shoes);
        var client = factory.CreateClient();
        var garmentIds = new[] { top.Id, bottom.Id, shoes.Id };

        foreach (var name in new[] { "", "   ", new string('a', 121) })
        {
            var response = await client.PostAsJsonAsync(
                "/api/outfits", new CreateOutfitRequest(name, null, garmentIds));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        Assert.Equal(0, await CountAsync(db => db.Outfits.CountAsync()));
    }

    [Fact]
    public async Task Create_rejects_dress_combined_with_a_top_or_bottom()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        var dress = TestData.NewGarment("Vestido", GarmentCategory.Dress);
        var outerwear = TestData.NewGarment("Campera", GarmentCategory.Outerwear);
        var shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);
        await TestData.SeedGarments(factory, top, bottom, dress, outerwear, shoes);
        var client = factory.CreateClient();

        Guid[][] invalidCombinations =
        [
            [dress.Id, top.Id, shoes.Id],
            [dress.Id, bottom.Id, shoes.Id]
        ];

        foreach (var garmentIds in invalidCombinations)
        {
            var response = await client.PostAsJsonAsync(
                "/api/outfits", new CreateOutfitRequest("Look superpuesto", null, garmentIds));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(
                "El outfit necesita una parte superior y una inferior, o un vestido.",
                await GetProblemDetailAsync(response));
        }

        Assert.Equal(0, await CountAsync(db => db.Outfits.CountAsync()));

        // A dress with outerwear (and shoes) still follows the valid alternative.
        var valid = await client.PostAsJsonAsync(
            "/api/outfits",
            new CreateOutfitRequest("Vestido con campera", null, [dress.Id, outerwear.Id, shoes.Id]));

        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
        var dto = await valid.Content.ReadFromJsonAsync<OutfitDto>();
        Assert.Equal([dress.Id, outerwear.Id, shoes.Id], dto!.Items.Select(i => i.Garment.Id));
    }

    [Fact]
    public async Task Create_rejects_occasion_over_sixty_characters()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        var shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);
        await TestData.SeedGarments(factory, top, bottom, shoes);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/outfits",
            new CreateOutfitRequest(
                "Look válido", new string('a', 61), [top.Id, bottom.Id, shoes.Id]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "La ocasión no puede superar los 60 caracteres.",
            await GetProblemDetailAsync(response));
        Assert.Equal(0, await CountAsync(db => db.Outfits.CountAsync()));
    }

    [Fact]
    public async Task Create_accepts_a_sixty_character_occasion()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        var shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);
        await TestData.SeedGarments(factory, top, bottom, shoes);
        var occasion = new string('o', 60);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/outfits",
            new CreateOutfitRequest("Look válido", occasion, [top.Id, bottom.Id, shoes.Id]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<OutfitDto>();
        Assert.Equal(occasion, dto!.Occasion);
    }

    [Fact]
    public async Task Create_rejects_formality_spread_greater_than_two()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        top.Formality = 1;
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        bottom.Formality = 4;
        var shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);
        shoes.Formality = 3;
        await TestData.SeedGarments(factory, top, bottom, shoes);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/outfits",
            new CreateOutfitRequest("Look disperso", null, [top.Id, bottom.Id, shoes.Id]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "La formalidad de las prendas no puede variar más de 2 puntos.",
            await GetProblemDetailAsync(response));
        Assert.Equal(0, await CountAsync(db => db.Outfits.CountAsync()));
    }

    [Fact]
    public async Task Create_returns_409_when_a_garment_is_archived()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        var archivedShoes = TestData.NewGarment(
            "Zapatillas viejas", GarmentCategory.Shoes, archived: true);
        await TestData.SeedGarments(factory, top, bottom, archivedShoes);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/outfits",
            new CreateOutfitRequest("Look con archivo", null, [top.Id, bottom.Id, archivedShoes.Id]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "Una o más prendas no existen o están archivadas.",
            await GetProblemDetailAsync(response));
        Assert.Equal(0, await CountAsync(db => db.Outfits.CountAsync()));
    }

    [Fact]
    public async Task Create_returns_409_when_a_garment_does_not_exist()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        await TestData.SeedGarments(factory, top, bottom);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/outfits",
            new CreateOutfitRequest("Look fantasma", null, [top.Id, bottom.Id, Guid.NewGuid()]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(0, await CountAsync(db => db.Outfits.CountAsync()));
    }

    [Fact]
    public async Task Patch_replaces_items_without_duplicating_the_outfit()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        var shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);
        var dress = TestData.NewGarment("Vestido", GarmentCategory.Dress);
        await TestData.SeedGarments(factory, top, bottom, shoes, dress);
        var client = factory.CreateClient();
        var created = await CreateOutfitAsync(client, "Look de día", [top.Id, bottom.Id, shoes.Id]);

        var response = await client.PatchAsJsonAsync(
            $"/api/outfits/{created.Id}",
            new PatchOutfitRequest("Look de noche", "cena", null, [dress.Id, shoes.Id]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<OutfitDto>();
        Assert.NotNull(dto);
        Assert.Equal(created.Id, dto!.Id);
        Assert.Equal("Look de noche", dto.Name);
        Assert.Equal("cena", dto.Occasion);
        Assert.Equal("manual", dto.Source);
        Assert.Equal([dress.Id, shoes.Id], dto.Items.Select(i => i.Garment.Id));
        Assert.Equal([0, 1], dto.Items.Select(i => i.SortOrder));
        Assert.Equal([0, 0], dto.Items.Select(i => i.Layer));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Outfits.CountAsync());
        Assert.Equal(2, await db.OutfitItems.CountAsync());
        Assert.Equal(4, await db.Garments.CountAsync());
    }

    [Fact]
    public async Task Patch_updates_metadata_only_and_keeps_the_items()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        var shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);
        await TestData.SeedGarments(factory, top, bottom, shoes);
        var client = factory.CreateClient();
        var created = await CreateOutfitAsync(client, "Look de día", [top.Id, bottom.Id, shoes.Id]);

        var response = await client.PatchAsJsonAsync(
            $"/api/outfits/{created.Id}",
            new PatchOutfitRequest("  Look nuevo  ", "  cena  ", true, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<OutfitDto>();
        Assert.NotNull(dto);
        Assert.Equal("Look nuevo", dto!.Name);
        Assert.Equal("cena", dto.Occasion);
        Assert.True(dto.IsFavorite);
        Assert.Equal([top.Id, bottom.Id, shoes.Id], dto.Items.Select(i => i.Garment.Id));

        var cleared = await client.PatchAsJsonAsync(
            $"/api/outfits/{created.Id}",
            new PatchOutfitRequest(null, "", null, null));

        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var clearedDto = await cleared.Content.ReadFromJsonAsync<OutfitDto>();
        Assert.Null(clearedDto!.Occasion);
        Assert.Equal("Look nuevo", clearedDto.Name);
        Assert.True(clearedDto.IsFavorite);
    }

    [Fact]
    public async Task Patch_metadata_only_keeps_an_archived_item()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        var shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);
        await TestData.SeedGarments(factory, top, bottom, shoes);
        var client = factory.CreateClient();
        var created = await CreateOutfitAsync(client, "Look de día", [top.Id, bottom.Id, shoes.Id]);

        // Archive a garment after the outfit was saved: the outfit must keep it.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var archived = await db.Garments.SingleAsync(g => g.Id == top.Id);
            archived.IsArchived = true;
            await db.SaveChangesAsync();
        }

        var response = await client.PatchAsJsonAsync(
            $"/api/outfits/{created.Id}",
            new PatchOutfitRequest("  Look archivado  ", null, true, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<OutfitDto>();
        Assert.NotNull(dto);
        Assert.Equal("Look archivado", dto!.Name);
        Assert.True(dto.IsFavorite);
        Assert.Equal([top.Id, bottom.Id, shoes.Id], dto.Items.Select(i => i.Garment.Id));

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(3, await verifyDb.OutfitItems.CountAsync(i => i.OutfitId == created.Id));
        Assert.True((await verifyDb.Garments.SingleAsync(g => g.Id == top.Id)).IsArchived);
    }

    [Fact]
    public async Task Patch_rejects_occasion_over_sixty_characters()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        var shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);
        await TestData.SeedGarments(factory, top, bottom, shoes);
        var client = factory.CreateClient();
        var created = await CreateOutfitAsync(client, "Look de día", [top.Id, bottom.Id, shoes.Id]);

        var response = await client.PatchAsJsonAsync(
            $"/api/outfits/{created.Id}",
            new PatchOutfitRequest(null, new string('a', 61), null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "La ocasión no puede superar los 60 caracteres.",
            await GetProblemDetailAsync(response));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null((await db.Outfits.AsNoTracking().SingleAsync(o => o.Id == created.Id)).Occasion);
    }

    [Fact]
    public async Task Patch_accepts_a_sixty_character_occasion()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        var shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);
        await TestData.SeedGarments(factory, top, bottom, shoes);
        var client = factory.CreateClient();
        var created = await CreateOutfitAsync(client, "Look de día", [top.Id, bottom.Id, shoes.Id]);
        var occasion = new string('o', 60);

        var response = await client.PatchAsJsonAsync(
            $"/api/outfits/{created.Id}",
            new PatchOutfitRequest(null, occasion, null, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<OutfitDto>();
        Assert.Equal(occasion, dto!.Occasion);
    }

    [Fact]
    public async Task Patch_returns_404_for_unknown_outfit()
    {
        var response = await factory.CreateClient().PatchAsJsonAsync(
            $"/api/outfits/{Guid.NewGuid()}",
            new PatchOutfitRequest("Fantasma", null, null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Patch_rejects_invalid_composition_and_keeps_the_previous_items()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        var shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);
        await TestData.SeedGarments(factory, top, bottom, shoes);
        var client = factory.CreateClient();
        var created = await CreateOutfitAsync(client, "Look de día", [top.Id, bottom.Id, shoes.Id]);

        var response = await client.PatchAsJsonAsync(
            $"/api/outfits/{created.Id}",
            new PatchOutfitRequest(null, null, null, [top.Id, bottom.Id]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("El outfit necesita calzado.", await GetProblemDetailAsync(response));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var itemIds = await db.OutfitItems
            .Where(i => i.OutfitId == created.Id)
            .OrderBy(i => i.SortOrder)
            .Select(i => i.GarmentId)
            .ToListAsync();
        Assert.Equal([top.Id, bottom.Id, shoes.Id], itemIds);
    }

    [Fact]
    public async Task Patch_returns_409_when_a_new_garment_is_archived()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        var shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);
        var archivedDress = TestData.NewGarment(
            "Vestido viejo", GarmentCategory.Dress, archived: true);
        await TestData.SeedGarments(factory, top, bottom, shoes, archivedDress);
        var client = factory.CreateClient();
        var created = await CreateOutfitAsync(client, "Look de día", [top.Id, bottom.Id, shoes.Id]);

        var response = await client.PatchAsJsonAsync(
            $"/api/outfits/{created.Id}",
            new PatchOutfitRequest(null, null, null, [archivedDress.Id, shoes.Id]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(3, await db.OutfitItems.CountAsync(i => i.OutfitId == created.Id));
    }

    [Fact]
    public async Task Patch_rejects_duplicate_garment_ids()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        var shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);
        await TestData.SeedGarments(factory, top, bottom, shoes);
        var client = factory.CreateClient();
        var created = await CreateOutfitAsync(client, "Look de día", [top.Id, bottom.Id, shoes.Id]);

        var response = await client.PatchAsJsonAsync(
            $"/api/outfits/{created.Id}",
            new PatchOutfitRequest(null, null, null, [top.Id, top.Id, bottom.Id, shoes.Id]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("No se puede repetir la misma prenda.", await GetProblemDetailAsync(response));
    }

    [Fact]
    public async Task Delete_removes_outfit_and_items_but_keeps_garments()
    {
        var top = TestData.NewGarment("Camisa", GarmentCategory.Top);
        var bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
        var shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);
        await TestData.SeedGarments(factory, top, bottom, shoes);
        var client = factory.CreateClient();
        var created = await CreateOutfitAsync(client, "Look de día", [top.Id, bottom.Id, shoes.Id]);

        var response = await client.DeleteAsync($"/api/outfits/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.Outfits.CountAsync());
        Assert.Equal(0, await db.OutfitItems.CountAsync());
        Assert.Equal(3, await db.Garments.CountAsync());

        var list = await client.GetFromJsonAsync<List<OutfitDto>>("/api/outfits");
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Delete_returns_404_for_unknown_outfit()
    {
        var response = await factory.CreateClient().DeleteAsync($"/api/outfits/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Writes_require_token()
    {
        using var securedFactory = new TestAppFactory { Token = "secreto" };
        var client = securedFactory.CreateClient();

        var post = await client.PostAsJsonAsync(
            "/api/outfits", new CreateOutfitRequest("Look", null, []));
        var patch = await client.PatchAsJsonAsync(
            $"/api/outfits/{Guid.NewGuid()}",
            new PatchOutfitRequest("Look", null, null, null));
        var delete = await client.DeleteAsync($"/api/outfits/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, patch.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
    }

    [Fact]
    public async Task Demo_blocks_outfit_writes_before_the_token_filter()
    {
        using var demoFactory = new TestAppFactory { Token = "secreto", Demo = true };
        var client = demoFactory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/outfits", new CreateOutfitRequest("Look", null, []));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private async Task<OutfitDto> CreateOutfitAsync(HttpClient client, string name, Guid[] garmentIds)
    {
        var response = await client.PostAsJsonAsync(
            "/api/outfits", new CreateOutfitRequest(name, null, garmentIds));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OutfitDto>())!;
    }

    private static async Task<string?> GetProblemDetailAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
    }

    private async Task<int> CountAsync(Func<AppDbContext, Task<int>> count)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await count(db);
    }
}
