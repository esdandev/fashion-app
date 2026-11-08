using FashionApp.Api.Infrastructure.Ai;

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

/// <summary>
/// Values currently stored for an already analyzed photo. The analyze response
/// includes them so re-uploading a saved photo opens the form with the real
/// values instead of the original AI snapshot.
/// </summary>
public sealed record CurrentGarmentValues(
    Guid Id,
    string Name,
    string Category,
    string? Subcategory,
    string ColorName,
    string ColorHex,
    string? Pattern,
    string[] Seasons,
    int Formality,
    string? Material,
    string[] StyleTags,
    string? Notes);

/// <summary>
/// Result of <c>POST /api/garments/analyze</c>. Nothing is persisted as a
/// <c>Garment</c>: the user must confirm before a row is created.
/// </summary>
public sealed record AnalyzeGarmentResponse(
    string ContentHash,
    string ImageUrl,
    string ThumbUrl,
    GarmentAnalysis Analysis,
    Guid? ExistingGarmentId,
    CurrentGarmentValues? CurrentGarment);

/// <summary>
/// Values confirmed by the user for an already analyzed photo. The image is
/// referenced by its content hash; no AI call happens on this request.
/// </summary>
public sealed record ConfirmGarmentRequest(
    string ContentHash,
    string Name,
    string? Category,
    string? Subcategory,
    string? ColorName,
    string? ColorHex,
    string? Pattern,
    string[]? Seasons,
    int? Formality,
    string? Material,
    string[]? StyleTags,
    string? Notes,
    double? Confidence);
