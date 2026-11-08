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

/// <summary>
/// Untrusted proposal produced by an <c>IDigestSummarizer</c>. Drafts must pass
/// <see cref="DigestValidator"/> before they are persisted: the model may invent
/// URLs or style tags that are not in the source feeds.
/// </summary>
public sealed record DigestDraft(
    string Title,
    string Url,
    string Source,
    string Summary,
    string[] StyleTags,
    string? ImageUrl,
    DateTimeOffset? PublishedAt);

/// <summary>
/// Result of summarizing the day's feed entries. <see cref="Summary"/> is the
/// short editorial intro and <see cref="Items"/> the proposed notes.
/// </summary>
public sealed record DigestSummary(string? Summary, IReadOnlyList<DigestDraft> Items);
