using FashionApp.Api.Domain;
using FashionApp.Api.Features.Outfits;
using FashionApp.Api.Infrastructure.Ai;

public sealed class OutfitSuggestionTests
{
    [Fact]
    public void Drops_drafts_with_invented_ids()
    {
        var wardrobe = new[] { Top(1), Bottom(2), Shoes(3) };
        var drafts = new[]
        {
            new OutfitDraft("Bien", new[] { wardrobe[0].Id, wardrobe[1].Id, wardrobe[2].Id }, null, "ok"),
            new OutfitDraft("Inventado", new[] { Guid.NewGuid() }, null, "mal"),
        };

        var kept = OutfitSuggestionValidator.KeepValid(drafts, wardrobe);
        var only = Assert.Single(kept);
        Assert.Equal("Bien", only.Name);
    }

    [Fact]
    public void Drops_drafts_with_duplicate_categories_using_known_ids()
    {
        var firstTop = Top(2);
        var secondTop = Top(3);
        var shoes = Shoes(2);
        var wardrobe = new[] { firstTop, secondTop, Bottom(2), shoes };
        var garmentIds = new[] { firstTop.Id, secondTop.Id, shoes.Id };

        // Every id resolves against the wardrobe, so the unknown-id branch cannot
        // be the reason it is dropped: the shared rules flag the duplicate top.
        Assert.All(garmentIds, id => Assert.Contains(id, wardrobe.Select(g => g.Id)));
        Assert.Equal(
            OutfitRules.DuplicateGarment,
            OutfitRules.Validate(new[] { firstTop, secondTop, shoes }).Error);

        var drafts = new[]
        {
            new OutfitDraft("Dos tops", garmentIds, null, "mal"),
        };

        var kept = OutfitSuggestionValidator.KeepValid(drafts, wardrobe);

        Assert.Empty(kept);
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

    [Theory]
    [InlineData("calor")]
    [InlineData("frio")]
    public async Task Treats_untagged_garments_as_season_agnostic(string weather)
    {
        var wardrobe = new[]
        {
            Top(3, seasons: ""),
            Bottom(3, seasons: ""),
            Shoes(3, seasons: "")
        };

        var drafts = await new RuleBasedOutfitSuggester().SuggestAsync(
            new OutfitSuggestionContext("casual", weather, wardrobe.Select(ToSummary).ToList()), default);

        var draft = Assert.Single(drafts);
        Assert.All(draft.GarmentIds, id => Assert.Contains(id, wardrobe.Select(g => g.Id)));
        Assert.True(OutfitRules.Validate(
            draft.GarmentIds.Select(id => wardrobe.Single(g => g.Id == id)).ToList()).IsValid);
    }

    [Fact]
    public async Task Rule_based_suggester_returns_at_most_three_outfits()
    {
        var wardrobe = new[]
        {
            Top(2), Top(3), Top(4),
            Bottom(2), Bottom(3), Bottom(4),
            Shoes(2), Shoes(3), Shoes(4)
        };

        var drafts = await new RuleBasedOutfitSuggester().SuggestAsync(
            new OutfitSuggestionContext("casual", "templado", wardrobe.Select(ToSummary).ToList()), default);

        Assert.Equal(3, drafts.Count);
    }

    [Fact]
    public async Task Rule_based_suggester_treats_unknown_weather_as_temperate()
    {
        var wardrobe = new[]
        {
            Top(3, seasons: "winter"),
            Bottom(3, seasons: "winter"),
            Shoes(3, seasons: "winter")
        };

        var drafts = await new RuleBasedOutfitSuggester().SuggestAsync(
            new OutfitSuggestionContext("casual", "granizo", wardrobe.Select(ToSummary).ToList()), default);

        Assert.Single(drafts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ocasión rara")]
    public async Task Accepts_null_or_unknown_occasion(string? occasion)
    {
        var wardrobe = new[] { Top(3), Bottom(3), Shoes(3) };

        var drafts = await new RuleBasedOutfitSuggester().SuggestAsync(
            new OutfitSuggestionContext(occasion, "templado", wardrobe.Select(ToSummary).ToList()), default);

        Assert.Single(drafts);
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
    public void Drops_a_draft_whose_base_stops_being_valid_after_normalization()
    {
        var dress = Dress(2);
        var top = Top(2);
        var bottom = Bottom(2);
        var shoes = Shoes(2);
        var bag = Garment("bag", GarmentCategory.Bag, 2, false, "spring,summer");
        var accessory = Garment("accessory", GarmentCategory.Accessory, 2, false, "spring,summer");
        var wardrobe = new[] { dress, top, bottom, shoes, bag, accessory };

        var draft = new OutfitDraft(
            "Seis piezas",
            new[] { dress.Id, top.Id, bottom.Id, shoes.Id, bag.Id, accessory.Id },
            null,
            "Al recortar a cinco queda un vestido con top y bottom.");

        var normalized = OutfitDraftNormalizer.NormalizeAll(new[] { draft });
        Assert.Equal(OutfitDraftNormalizer.MaxGarmentIds, Assert.Single(normalized).GarmentIds.Length);

        // A dress is an alternative to separates, so truncating the sixth id is
        // not enough: the shared rules drop the proposal instead of repairing it.
        Assert.Empty(OutfitSuggestionValidator.KeepValid(normalized, wardrobe));
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
