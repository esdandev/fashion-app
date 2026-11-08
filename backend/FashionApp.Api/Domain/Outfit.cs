namespace FashionApp.Api.Domain;

public enum OutfitSource
{
    Ai,
    Manual
}

/// <summary>
/// Minimal outfit entity so the initial migration already owns the table.
/// The outfit feature itself arrives in a later phase.
/// </summary>
public sealed class Outfit
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Occasion { get; set; }
    public OutfitSource Source { get; set; } = OutfitSource.Manual;
    public string? Rationale { get; set; }
    public bool IsFavorite { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<OutfitItem> Items { get; set; } = [];
}
