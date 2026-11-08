using FashionApp.Api.Domain;
using FashionApp.Api.Features.Outfits;
using FashionApp.Api.Infrastructure.Ai;

public sealed class OutfitSuggestionTests
{
    [Fact]
    public void Drops_drafts_with_invented_ids_and_duplicate_categories()
    {
        var wardrobe = new[] { Top(1), Bottom(2), Shoes(3) };
        var drafts = new[]
        {
            new OutfitDraft("Bien", new[] { wardrobe[0].Id, wardrobe[1].Id, wardrobe[2].Id }, null, "ok"),
            new OutfitDraft("Inventado", new[] { Guid.NewGuid() }, null, "mal"),
            new OutfitDraft("Dos tops", new[] { wardrobe[0].Id, Top(4).Id, wardrobe[2].Id }, null, "mal"),
        };

        var kept = OutfitSuggestionValidator.KeepValid(drafts, wardrobe);
        Assert.Single(kept);
        Assert.Equal("Bien", kept[0].Name);
    }

    [Fact]
    public async Task Rule_based_suggester_returns_valid_outfits()
    {
        var wardrobe = new[] { Top(2), Bottom(2), Shoes(2), Dress(3), Outerwear(2) };
        var drafts = await new RuleBasedOutfitSuggester().SuggestAsync(
            new OutfitSuggestionContext("trabajo", "templado", wardrobe.Select(ToSummary).ToList()), default);

        Assert.NotEmpty(drafts);
        Assert.All(drafts, d => Assert.True(OutfitRules.Validate(
            d.GarmentIds.Select(id => wardrobe.Single(g => g.Id == id)).ToList()).IsValid));
    }

    [Fact]
    public void Drops_archived_garments_even_when_the_id_exists()
    {
        var archived = Top(2, archived: true);
        var bottom = Bottom(2);
        var shoes = Shoes(2);
        var drafts = new[]
        {
            new OutfitDraft(
                "Con archivada",
                new[] { archived.Id, bottom.Id, shoes.Id },
                null,
                "no debería quedar"),
        };

        var kept = OutfitSuggestionValidator.KeepValid(drafts, new[] { archived, bottom, shoes });

        Assert.Empty(kept);
    }

    [Fact]
    public void Deduplicates_drafts_with_the_same_garment_set()
    {
        var wardrobe = new[] { Top(2), Bottom(2), Shoes(2) };
        var ids = new[] { wardrobe[0].Id, wardrobe[1].Id, wardrobe[2].Id };
        var drafts = new[]
        {
            new OutfitDraft("Primero", ids, null, "ok"),
            new OutfitDraft("Repetido", ids.Reverse().ToArray(), null, "mismo conjunto"),
        };

        var kept = OutfitSuggestionValidator.KeepValid(drafts, wardrobe);

        var only = Assert.Single(kept);
        Assert.Equal("Primero", only.Name);
    }

    [Fact]
    public async Task Rule_based_suggester_returns_empty_when_there_is_no_valid_combination()
    {
        var wardrobe = new[] { Top(3), Bottom(3) }; // falta calzado

        var drafts = await new RuleBasedOutfitSuggester().SuggestAsync(
            new OutfitSuggestionContext("casual", "templado", wardrobe.Select(ToSummary).ToList()), default);

        Assert.Empty(drafts);
    }

    [Fact]
    public async Task Rule_based_suggester_prefers_garments_close_to_the_occasion_formality()
    {
        var formalTop = Top(4);
        var casualTop = Top(1);
        var wardrobe = new[] { formalTop, casualTop, Bottom(4), Shoes(4) };

        var drafts = await new RuleBasedOutfitSuggester().SuggestAsync(
            new OutfitSuggestionContext("trabajo", "templado", wardrobe.Select(ToSummary).ToList()), default);

        Assert.NotEmpty(drafts);
        Assert.Contains(formalTop.Id, drafts[0].GarmentIds);
        Assert.DoesNotContain(casualTop.Id, drafts[0].GarmentIds);
    }

    [Fact]
    public async Task Rule_based_suggester_adds_outerwear_when_the_weather_is_cold()
    {
        var outerwear = Outerwear(3, seasons: "winter,autumn");
        var wardrobe = new[]
        {
            Top(3, seasons: "winter,autumn"),
            Bottom(3, seasons: "winter,autumn"),
            Shoes(3, seasons: "winter,autumn"),
            outerwear
        };

        var drafts = await new RuleBasedOutfitSuggester().SuggestAsync(
            new OutfitSuggestionContext("trabajo", "frio", wardrobe.Select(ToSummary).ToList()), default);

        Assert.NotEmpty(drafts);
        Assert.All(drafts, d => Assert.Contains(outerwear.Id, d.GarmentIds));
    }

    [Fact]
    public async Task Rule_based_suggester_ignores_garments_from_another_season()
    {
        var winterTop = Top(3, seasons: "winter");
        var summerTop = Top(3, seasons: "summer");
        var wardrobe = new[] { winterTop, summerTop, Bottom(3, seasons: "summer"), Shoes(3, seasons: "summer") };

        var drafts = await new RuleBasedOutfitSuggester().SuggestAsync(
            new OutfitSuggestionContext("casual", "calor", wardrobe.Select(ToSummary).ToList()), default);

        Assert.NotEmpty(drafts);
        Assert.All(drafts, d => Assert.DoesNotContain(winterTop.Id, d.GarmentIds));
    }

    private static Garment Top(int formality = 3, bool archived = false, string seasons = "spring,summer") =>
        Garment("top", GarmentCategory.Top, formality, archived, seasons);

    private static Garment Bottom(int formality = 3, bool archived = false, string seasons = "spring,summer") =>
        Garment("bottom", GarmentCategory.Bottom, formality, archived, seasons);

    private static Garment Shoes(int formality = 3, bool archived = false, string seasons = "spring,summer") =>
        Garment("shoes", GarmentCategory.Shoes, formality, archived, seasons);

    private static Garment Dress(int formality = 3, bool archived = false, string seasons = "spring,summer") =>
        Garment("dress", GarmentCategory.Dress, formality, archived, seasons);

    private static Garment Outerwear(int formality = 3, bool archived = false, string seasons = "spring,summer") =>
        Garment("outerwear", GarmentCategory.Outerwear, formality, archived, seasons);

    private static Garment Garment(
        string name,
        GarmentCategory category,
        int formality,
        bool archived,
        string seasons)
    {
        var garment = TestData.NewGarment(name, category, archived);
        garment.Formality = formality;
        garment.Seasons = seasons;
        return garment;
    }

    private static GarmentSummary ToSummary(Garment garment) => new(
        garment.Id,
        garment.Category.ToString().ToLowerInvariant(),
        garment.ColorName,
        garment.Formality,
        garment.Seasons.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        []);
}
