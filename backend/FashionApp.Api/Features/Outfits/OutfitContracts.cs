using FashionApp.Api.Features.Garments;

namespace FashionApp.Api.Features.Outfits;

public sealed record OutfitItemDto(GarmentDto Garment, int Layer, int SortOrder);

public sealed record OutfitDto(
    Guid Id,
    string Name,
    string? Occasion,
    string Source,
    string? Rationale,
    bool IsFavorite,
    DateTimeOffset CreatedAt,
    IReadOnlyList<OutfitItemDto> Items);

/// <summary>
/// Manual outfit creation. <c>GarmentIds</c> is kept in request order: that
/// order becomes <c>OutfitItem.SortOrder</c>. <c>Source</c> defaults to
/// <c>manual</c>; saving an AI proposal sends <c>ai</c> plus its rationale.
/// </summary>
public sealed record CreateOutfitRequest(
    string Name,
    string? Occasion,
    Guid[] GarmentIds,
    string? Source = null,
    string? Rationale = null);

/// <summary>
/// Partial update. A <c>null</c> field means "leave unchanged"; an empty
/// <c>Occasion</c> clears it. When <c>GarmentIds</c> is present the whole item
/// set is replaced and revalidated.
/// </summary>
public sealed record PatchOutfitRequest(
    string? Name,
    string? Occasion,
    bool? IsFavorite,
    Guid[]? GarmentIds);
