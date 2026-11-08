namespace FashionApp.Api.Domain;

/// <summary>
/// Daily usage counter for cost-controlled operations (currently only AI
/// analysis). The composite key is <c>(Date, Kind)</c>; <c>Date</c> is always a
/// UTC calendar date.
/// </summary>
public sealed class UsageCounter
{
    public DateOnly Date { get; set; }
    public string Kind { get; set; } = "";
    public int Count { get; set; }
}
