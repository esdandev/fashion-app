namespace FashionApp.Api.Features.Garments;

public sealed record GarmentDto(
    Guid Id,
    string Name,
    string Category,
    string ColorName,
    string ColorHex,
    int Formality,
    double Confidence,
    string[] Seasons,
    string[] StyleTags,
    string ImageUrl,
    string ThumbUrl);

public sealed record GarmentListResponse(IReadOnlyList<GarmentDto> Items, string? NextCursor);

public sealed record PatchGarmentRequest(
    string? Name,
    string? Category,
    string? ColorName,
    string? ColorHex,
    int? Formality,
    string[]? Seasons,
    string[]? StyleTags,
    string? Notes);
