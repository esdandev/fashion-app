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
