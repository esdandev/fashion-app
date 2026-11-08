using FashionApp.Api.Features.Garments;

public sealed class GarmentInputRulesTests
{
    [Fact]
    public void Accepts_values_inside_the_closed_vocabulary()
    {
        var error = GarmentInputRules.Validate(
            "Camisa de lino",
            requireName: true,
            "top",
            "camisa",
            "beige",
            "#D8C3A5",
            "liso",
            "lino",
            "Notas",
            3,
            ["spring", "summer"],
            ["minimalista", "casual"]);

        Assert.Null(error);
    }

    [Theory]
    [InlineData("no-es-un-hex")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    public void Rejects_invalid_color_hex(string colorHex)
    {
        var error = GarmentInputRules.Validate(
            "Camisa", true, null, null, null, colorHex, null, null, null, null, null, null);

        Assert.Equal("El color hexadecimal debe tener el formato #RRGGBB.", error);
    }

    [Fact]
    public void Rejects_unknown_seasons_and_tags()
    {
        var seasonError = GarmentInputRules.Validate(
            "Camisa", true, null, null, null, null, null, null, null, null,
            ["monsoon"], null);
        var tagError = GarmentInputRules.Validate(
            "Camisa", true, null, null, null, null, null, null, null, null,
            null, ["inventado"]);

        Assert.Contains("temporada 'monsoon'", seasonError);
        Assert.Contains("etiqueta 'inventado'", tagError);
    }

    [Fact]
    public void Rejects_more_than_four_tags_after_deduplication()
    {
        var error = GarmentInputRules.Validate(
            "Camisa", true, null, null, null, null, null, null, null, null, null,
            ["minimalista", "casual", "formal", "vintage", "y2k"]);

        Assert.Equal("Podés elegir hasta 4 etiquetas.", error);
    }

    [Fact]
    public void Rejects_empty_and_overlong_names()
    {
        var emptyError = GarmentInputRules.Validate(
            "   ", true, null, null, null, null, null, null, null, null, null, null);
        var longError = GarmentInputRules.Validate(
            new string('a', 121), true, null, null, null, null, null, null,
            null, null, null, null);

        Assert.Equal("El nombre no puede estar vacío.", emptyError);
        Assert.Contains("120", longError);
    }

    [Fact]
    public void Rejects_text_over_the_column_bounds()
    {
        Assert.Contains(
            "notas",
            GarmentInputRules.Validate(
                "Camisa", true, null, null, null, null, null, null,
                new string('a', 401), null, null, null));

        Assert.Contains(
            "color",
            GarmentInputRules.Validate(
                "Camisa", true, null, null, new string('a', 41), null, null, null,
                null, null, null, null));
    }

    [Fact]
    public void Normalizes_lists_by_trimming_deduplicating_and_lowercasing()
    {
        Assert.Equal(
            ["summer", "winter"],
            GarmentInputRules.NormalizeSeasons([" Summer ", "summer", "WINTER"]));
        Assert.Equal(
            ["minimalista", "casual"],
            GarmentInputRules.NormalizeStyleTags([" Minimalista ", "minimalista", "Casual"]));
    }
}
