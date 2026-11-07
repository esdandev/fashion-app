namespace FashionApp.Api.Infrastructure.Ai;

public interface IGarmentAnalyzer
{
    Task<GarmentAnalysis> AnalyzeAsync(byte[] image, string contentType, CancellationToken ct);
}
