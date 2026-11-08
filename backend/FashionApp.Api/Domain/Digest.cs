namespace FashionApp.Api.Domain;

/// <summary>
/// One daily fashion digest, generated from the configured feeds. There is at
/// most one row per calendar date; re-generating the same day replaces it.
/// </summary>
public sealed class Digest
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public string Summary { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; }
    public List<DigestItem> Items { get; set; } = [];
}
