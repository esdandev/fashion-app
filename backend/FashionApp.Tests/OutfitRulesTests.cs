using FashionApp.Api.Domain;
using FashionApp.Api.Features.Outfits;

public sealed class OutfitRulesTests
{
    [Theory]
    [InlineData("top,bottom,shoes", true)]
    [InlineData("dress,shoes", true)]
    [InlineData("top,shoes", false)]                 // falta bottom
    [InlineData("top,bottom", false)]                // falta shoes
    [InlineData("top,top,bottom,shoes", false)]      // dos tops
    [InlineData("top,bottom,shoes,outerwear,outerwear", false)]
    [InlineData("dress,top,shoes", false)]           // vestido + parte superior
    [InlineData("dress,bottom,shoes", false)]        // vestido + parte inferior
    [InlineData("dress,outerwear,shoes", true)]      // abrigo permitido con vestido
    public void Validates_category_composition(string csv, bool expected)
    {
        var garments = csv.Split(',').Select(ToGarment).ToList();
        Assert.Equal(expected, OutfitRules.Validate(garments).IsValid);
    }

    [Fact]
    public void Rejects_formality_spread_greater_than_two()
    {
        var garments = new[] { ToGarment("top", 1), ToGarment("bottom", 4), ToGarment("shoes", 3) };
        Assert.False(OutfitRules.Validate(garments).IsValid);
    }

    [Theory]
    [InlineData("top,shoes", "El outfit necesita una parte superior y una inferior, o un vestido.")]
    [InlineData("top,bottom", "El outfit necesita calzado.")]
    [InlineData("dress", "El outfit necesita calzado.")]
    [InlineData("bottom,shoes", "El outfit necesita una parte superior y una inferior, o un vestido.")]
    [InlineData("dress,top,shoes", "El outfit necesita una parte superior y una inferior, o un vestido.")]
    [InlineData("dress,bottom,shoes", "El outfit necesita una parte superior y una inferior, o un vestido.")]
    [InlineData("top,top,bottom,shoes", "No se puede repetir la misma prenda.")]
    [InlineData("top,bottom,bottom,shoes", "No se puede repetir la misma prenda.")]
    [InlineData("dress,dress,shoes", "No se puede repetir la misma prenda.")]
    [InlineData("top,bottom,shoes,outerwear,outerwear", "Solo puede haber un abrigo.")]
    public void Returns_the_expected_spanish_error(string csv, string expectedError)
    {
        var garments = csv.Split(',').Select(ToGarment).ToList();

        var result = OutfitRules.Validate(garments);

        Assert.False(result.IsValid);
        Assert.Equal(expectedError, result.Error);
    }

    [Fact]
    public void Returns_no_error_for_a_valid_outfit()
    {
        var garments = new[] { ToGarment("top"), ToGarment("bottom"), ToGarment("shoes") };

        var result = OutfitRules.Validate(garments);

        Assert.True(result.IsValid);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Rejects_formality_spread_with_the_exact_message()
    {
        var garments = new[] { ToGarment("top", 1), ToGarment("bottom", 4), ToGarment("shoes", 3) };

        var result = OutfitRules.Validate(garments);

        Assert.False(result.IsValid);
        Assert.Equal("La formalidad de las prendas no puede variar más de 2 puntos.", result.Error);
    }

    [Fact]
    public void Allows_formality_spread_of_exactly_two()
    {
        var garments = new[] { ToGarment("top", 2), ToGarment("bottom", 4), ToGarment("shoes", 3) };

        Assert.True(OutfitRules.Validate(garments).IsValid);
    }

    [Fact]
    public void Rejects_the_same_garment_twice()
    {
        var top = ToGarment("top");
        var garments = new[] { top, ToGarment("bottom"), ToGarment("shoes"), top };

        var result = OutfitRules.Validate(garments);

        Assert.False(result.IsValid);
        Assert.Equal("No se puede repetir la misma prenda.", result.Error);
    }

    [Fact]
    public void Allows_multiple_bags_and_accessories()
    {
        var garments = new[]
        {
            ToGarment("top"),
            ToGarment("bottom"),
            ToGarment("shoes"),
            ToGarment("bag"),
            ToGarment("bag"),
            ToGarment("accessory"),
            ToGarment("accessory")
        };

        Assert.True(OutfitRules.Validate(garments).IsValid);
    }

    private static Garment ToGarment(string value, int formality = 3)
    {
        var category = Enum.Parse<GarmentCategory>(value, ignoreCase: true);
        var garment = TestData.NewGarment(value, category);
        garment.Formality = formality;
        return garment;
    }
}
