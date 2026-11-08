using FashionApp.Api.Features.Garments;

namespace FashionApp.Api.Features.Digest;

/// <summary>
/// Read model for <c>GET /api/digest/today</c>. <see cref="DigestItemDto.RelatedGarments"/>
/// is only populated when the request may see personal data (open instance or a
/// valid <c>X-App-Token</c>); otherwise it is always an empty list.
/// </summary>
public sealed record DigestItemDto(
    Guid Id,
    string Title,
    string Url,
    string Source,
    string Summary,
    string[] StyleTags,
    string? ImageUrl,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<GarmentDto> RelatedGarments);

public sealed record DigestDto(
    DateOnly Date,
    string Summary,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<DigestItemDto> Items);
