namespace FashionApp.Api.Infrastructure.Ai;

/// <summary>
/// Deterministic analyzer used in tests and local development. Returns a fixed
/// beige linen shirt so the whole upload flow can run without network calls.
/// </summary>
public sealed class FakeGarmentAnalyzer : IGarmentAnalyzer
{
    public Task<GarmentAnalysis> AnalyzeAsync(
        byte[] image,
        string contentType,
        CancellationToken ct) =>
        Task.FromResult(new GarmentAnalysis(
            Name: "Camisa de lino",
            Category: "top",
            Subcategory: "camisa",
            Colors: ["beige"],
            Pattern: "liso",
            Seasons: ["spring", "summer"],
            Formality: 3,
            Material: "lino",
            StyleTags: ["minimalista", "casual"],
            Confidence: 0.9));
}
