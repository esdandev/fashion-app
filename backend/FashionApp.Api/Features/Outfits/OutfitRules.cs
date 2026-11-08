using FashionApp.Api.Domain;

namespace FashionApp.Api.Features.Outfits;

/// <summary>
/// Result of validating a proposed outfit. <see cref="Error"/> is a user-facing
/// Spanish message and stays <c>null</c> for valid outfits.
/// </summary>
public sealed record OutfitValidation(bool IsValid, string? Error)
{
    public static readonly OutfitValidation Valid = new(true, null);

    public static OutfitValidation Invalid(string error) => new(false, error);
}

/// <summary>
/// Composition rules shared by manual CRUD, the rule-based suggester and the AI
/// suggestion validator. Only the garments the caller passes are considered:
/// resolving ids and excluding archived garments is the caller's responsibility.
/// </summary>
public static class OutfitRules
{
    public const string InvalidComposition =
        "El outfit necesita una parte superior y una inferior, o un vestido.";

    public const string MissingShoes = "El outfit necesita calzado.";

    public const string MultipleOuterwear = "Solo puede haber un abrigo.";

    public const string DuplicateGarment = "No se puede repetir la misma prenda.";

    public const string FormalitySpread =
        "La formalidad de las prendas no puede variar más de 2 puntos.";

    public static OutfitValidation Validate(IReadOnlyCollection<Garment> garments)
    {
        if (garments is null || garments.Count == 0)
            return OutfitValidation.Invalid(InvalidComposition);

        // The same physical garment cannot appear twice (the OutfitItems table
        // has a composite PK, so allowing it would fail at persistence time).
        if (garments.Select(g => g.Id).Distinct().Count() != garments.Count)
            return OutfitValidation.Invalid(DuplicateGarment);

        var topCount = garments.Count(g => g.Category == GarmentCategory.Top);
        var bottomCount = garments.Count(g => g.Category == GarmentCategory.Bottom);
        var dressCount = garments.Count(g => g.Category == GarmentCategory.Dress);
        var shoesCount = garments.Count(g => g.Category == GarmentCategory.Shoes);
        var outerwearCount = garments.Count(g => g.Category == GarmentCategory.Outerwear);

        // One of each base piece at most: two tops, two bottoms or two dresses
        // are a repeated garment category even when the ids differ.
        if (topCount > 1 || bottomCount > 1 || dressCount > 1)
            return OutfitValidation.Invalid(DuplicateGarment);

        // A dress is an alternative to separates, never an addition: combining it
        // with a top or a bottom produces an over-layered, invalid outfit.
        var hasDress = dressCount == 1;
        var hasTopAndBottom = topCount == 1 && bottomCount == 1;
        if (hasDress && (topCount > 0 || bottomCount > 0))
            return OutfitValidation.Invalid(InvalidComposition);
        if (!hasDress && !hasTopAndBottom)
            return OutfitValidation.Invalid(InvalidComposition);

        if (shoesCount == 0)
            return OutfitValidation.Invalid(MissingShoes);

        if (outerwearCount > 1)
            return OutfitValidation.Invalid(MultipleOuterwear);

        var spread = garments.Max(g => g.Formality) - garments.Min(g => g.Formality);
        if (spread > 2)
            return OutfitValidation.Invalid(FormalitySpread);

        return OutfitValidation.Valid;
    }
}
