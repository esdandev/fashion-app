namespace FashionApp.Api.Domain;

/// <summary>
/// One news item inside a <see cref="Digest"/>. Only the title, a short
/// summary written for this app and the link to the source are stored; the
/// article body is never copied.
/// </summary>
public sealed class DigestItem
{
    public Guid Id { get; set; }
    public Guid DigestId { get; set; }
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Source { get; set; } = "";
    public string Summary { get; set; } = "";
    public string StyleTagsJson { get; set; } = "";
    public string? ImageUrl { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int SortOrder { get; set; }
    public Digest Digest { get; set; } = null!;
}
