namespace FashionApp.Api.Domain;

/// <summary>
/// Join row between an <see cref="Outfit"/> and one of its <see cref="Garment"/>
/// pieces. <see cref="Layer"/> drives the collage stacking (base, outerwear,
/// accessories) and <see cref="SortOrder"/> preserves the saved order.
/// </summary>
public sealed class OutfitItem
{
    public Guid OutfitId { get; set; }
    public Guid GarmentId { get; set; }
    public int Layer { get; set; }
    public int SortOrder { get; set; }
    public Outfit Outfit { get; set; } = null!;
    public Garment Garment { get; set; } = null!;
}
