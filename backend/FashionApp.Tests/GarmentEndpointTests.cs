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
    public async Task List_orders_by_created_at_desc_across_pages()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-10);
        var garments = Enumerable.Range(0, 30)
            .Select(i =>
            {
                var garment = NewGarment($"Prenda {i}", GarmentCategory.Top);
                garment.CreatedAt = start.AddMinutes(i);
                garment.UpdatedAt = garment.CreatedAt;
                return garment;
            })
            .ToArray();
        await SeedGarments(factory, garments);
        var expected = garments
            .OrderByDescending(g => g.CreatedAt)
            .Select(g => g.Id)
            .ToArray();

        var client = factory.CreateClient();
        var first = await client.GetFromJsonAsync<GarmentListResponse>("/api/garments");
        var second = await client.GetFromJsonAsync<GarmentListResponse>(
            $"/api/garments?cursor={first!.NextCursor}");

        Assert.Equal(24, first.Items.Count);
        Assert.NotNull(second);
        Assert.Equal(6, second!.Items.Count);
        Assert.Equal(expected, first.Items.Concat(second.Items).Select(i => i.Id));
    }

    [Fact]
    public async Task List_filters_by_color_only()
    {
        var beigeSpring = NewGarment("Camisa", GarmentCategory.Top);
        beigeSpring.ColorName = "Beige";
        beigeSpring.Seasons = "spring,summer";

        var negroWinter = NewGarment("Abrigo", GarmentCategory.Outerwear);
        negroWinter.ColorName = "negro";
        negroWinter.Seasons = "autumn,winter";

        var beigeWinter = NewGarment("Jersey", GarmentCategory.Top);
        beigeWinter.ColorName = "beige";
        beigeWinter.Seasons = "autumn,winter";

        await SeedGarments(factory, beigeSpring, negroWinter, beigeWinter);

        var body = await factory.CreateClient()
            .GetFromJsonAsync<GarmentListResponse>("/api/garments?color=BEIGE");

        Assert.Equal(
            new[] { "Camisa", "Jersey" },
            body!.Items.Select(i => i.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task List_filters_by_season_only()
    {
        var beigeSpring = NewGarment("Camisa", GarmentCategory.Top);
        beigeSpring.ColorName = "Beige";
        beigeSpring.Seasons = "spring,summer";

        var negroWinter = NewGarment("Abrigo", GarmentCategory.Outerwear);
        negroWinter.ColorName = "negro";
        negroWinter.Seasons = "autumn,winter";

        var beigeWinter = NewGarment("Jersey", GarmentCategory.Top);
        beigeWinter.ColorName = "beige";
        beigeWinter.Seasons = "autumn,winter";

        await SeedGarments(factory, beigeSpring, negroWinter, beigeWinter);

        var body = await factory.CreateClient()
            .GetFromJsonAsync<GarmentListResponse>("/api/garments?season=Winter");

        Assert.Equal(
            new[] { "Abrigo", "Jersey" },
            body!.Items.Select(i => i.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task List_returns_next_cursor_only_after_24_items()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-5);
        var initial = Enumerable.Range(0, 24)
            .Select(i =>
            {
                var garment = NewGarment($"Prenda {i}", GarmentCategory.Top);
                garment.CreatedAt = start.AddMinutes(i);
                garment.UpdatedAt = garment.CreatedAt;
                return garment;
            })
            .ToArray();
        await SeedGarments(factory, initial);

        var client = factory.CreateClient();
        var exact = await client.GetFromJsonAsync<GarmentListResponse>("/api/garments");
        Assert.Equal(24, exact!.Items.Count);
        Assert.Null(exact.NextCursor);

        var extra = NewGarment("Extra", GarmentCategory.Top);
        extra.CreatedAt = start.AddMinutes(24);
        extra.UpdatedAt = extra.CreatedAt;
        await SeedGarments(factory, extra);

        var firstPage = await client.GetFromJsonAsync<GarmentListResponse>("/api/garments");
        Assert.Equal(24, firstPage!.Items.Count);
        Assert.NotNull(firstPage.NextCursor);

        var secondPage = await client.GetFromJsonAsync<GarmentListResponse>(
            $"/api/garments?cursor={firstPage.NextCursor}");
        Assert.NotNull(secondPage);
        Assert.Single(secondPage!.Items);
        Assert.Null(secondPage.NextCursor);
        Assert.Equal(25, firstPage.Items.Concat(secondPage.Items).Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public async Task List_normalizes_non_utc_offsets_to_utc()
    {
        var oldUtc = DateTimeOffset.UtcNow.AddHours(-3);
        var oldLocal = NewGarment("Antiguo", GarmentCategory.Top);
        oldLocal.CreatedAt = oldUtc.ToOffset(TimeSpan.FromHours(-5));
        oldLocal.UpdatedAt = oldLocal.CreatedAt;

        var newest = NewGarment("Nuevo", GarmentCategory.Top);
        newest.CreatedAt = DateTimeOffset.UtcNow;
        newest.UpdatedAt = newest.CreatedAt;

        await SeedGarments(factory, oldLocal, newest);

        var body = await factory.CreateClient().GetFromJsonAsync<GarmentListResponse>("/api/garments");
        Assert.Equal(new[] { "Nuevo", "Antiguo" }, body!.Items.Select(i => i.Name));

        var saved = await GetGarment(oldLocal.Id);
        Assert.NotNull(saved);
        Assert.Equal(TimeSpan.Zero, saved!.CreatedAt.Offset);
        Assert.Equal(oldUtc.UtcTicks, saved.CreatedAt.UtcTicks);
    }

    [Fact]
    public async Task List_ordering_uses_created_at_id_index()
    {
        await SeedGarments(factory, Enumerable.Range(0, 30)
            .Select(i =>
            {
                var garment = NewGarment($"Prenda {i}", GarmentCategory.Top);
                garment.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-i);
                garment.UpdatedAt = garment.CreatedAt;
                return garment;
            })
            .ToArray());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.OpenConnectionAsync();

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            EXPLAIN QUERY PLAN
            SELECT "g"."Id", "g"."CreatedAt"
            FROM "Garments" AS "g"
            WHERE NOT ("g"."IsArchived")
            ORDER BY "g"."CreatedAt" DESC, "g"."Id" DESC
            LIMIT 25
            """;

        var plan = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            plan.Add(reader.GetString(3));

        Assert.Contains(
            plan,
            detail => detail.Contains("IX_Garments_CreatedAt_Id", StringComparison.Ordinal));
        Assert.DoesNotContain(
            plan,
            detail => detail.Contains("TEMP B-TREE", StringComparison.OrdinalIgnoreCase));
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
    public async Task Patch_rejects_input_outside_the_closed_vocabulary_and_bounds()
    {
        var id = await SeedOneGarment();
        var client = factory.CreateClient();

        PatchGarmentRequest[] cases =
        [
            new(null, null, null, "rojo", null, null, null, null),
            new(null, null, null, "#12345", null, null, null, null),
            new(null, null, null, null, null, ["monsoon"], null, null),
            new(null, null, null, null, null, null, ["inventado"], null),
            new(null, null, null, null, null, null,
                ["minimalista", "casual", "formal", "vintage", "y2k"], null),
            new(new string('a', 121), null, null, null, null, null, null, null),
            new(null, null, new string('a', 41), null, null, null, null, null),
            new(null, null, null, null, null, null, null, new string('a', 401))
        ];

        foreach (var request in cases)
        {
            var response = await client.PatchAsJsonAsync($"/api/garments/{id}", request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        var saved = await GetGarment(id);
        Assert.NotNull(saved);
        Assert.Equal("Camisa de lino", saved!.Name);
        Assert.Equal("beige", saved.ColorName);
        Assert.Equal("spring,summer", saved.Seasons);
        Assert.Equal("", saved.StyleTagsJson);
    }

    [Fact]
    public async Task Patch_normalizes_seasons_tags_and_text()
    {
        var id = await SeedOneGarment();
        var request = new PatchGarmentRequest(
            null, null, " Blanco ", "#ABCDEF", null,
            ["Summer", "summer", "Winter"],
            [" Minimalista ", "minimalista", "Casual"],
            "  nota  ");

        var response = await factory.CreateClient()
            .PatchAsJsonAsync($"/api/garments/{id}", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<GarmentDto>();
        Assert.Equal("Blanco", dto!.ColorName);
        Assert.Equal("#ABCDEF", dto.ColorHex);
        Assert.Equal(new[] { "summer", "winter" }, dto.Seasons);
        Assert.Equal(new[] { "minimalista", "casual" }, dto.StyleTags);

        var saved = await GetGarment(id);
        Assert.Equal("summer,winter", saved!.Seasons);
        Assert.Equal("""["minimalista","casual"]""", saved.StyleTagsJson);
        Assert.Equal("nota", saved.Notes);
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
