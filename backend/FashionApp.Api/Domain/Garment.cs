namespace FashionApp.Api.Domain;

/// <summary>
/// A single piece of clothing in the wardrobe. Removal is always logical
/// through <see cref="IsArchived"/>; this phase has no physical deletes.
/// </summary>
public sealed class Garment
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public GarmentCategory Category { get; set; } = GarmentCategory.Top;
    public string? Subcategory { get; set; }
    public string ColorName { get; set; } = "";
    public string ColorHex { get; set; } = "";
    public string? SecondaryColorsJson { get; set; }
    public string? Pattern { get; set; }
    public string Seasons { get; set; } = "";
    public int Formality { get; set; } = 1;
    public string? Material { get; set; }
    public string? Brand { get; set; }
    public string StyleTagsJson { get; set; } = "";
    public string? Notes { get; set; }
    public string ImagePath { get; set; } = "";
    public string ThumbPath { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string? AnalysisJson { get; set; }
    public double Confidence { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsArchived { get; set; }
}
