using FashionApp.Api.Infrastructure.Ai;
using Microsoft.Extensions.Options;

public sealed class GarmentAnalysisTests
{
    [Fact]
    public void Validator_discards_unknown_tags_and_clamps_formality()
    {
        var raw = new GarmentAnalysis("Camisa", "top", null,
            ["beige"], null, ["summer"], 9, null, ["minimalista", "inventado"], 0.8);

        Assert.True(GarmentAnalysisValidator.TryNormalize(raw, out var clean));
        Assert.Equal(5, clean.Formality);
        Assert.Equal(new[] { "minimalista" }, clean.StyleTags);
    }

    [Fact]
    public void Validator_falls_back_to_top_and_default_name()
    {
        var raw = new GarmentAnalysis("   ", "sombrero", null,
            [], null, ["monsoon"], 0, null, [], 0.4);

        Assert.True(GarmentAnalysisValidator.TryNormalize(raw, out var clean));
        Assert.Equal("Prenda", clean.Name);
        Assert.Equal("top", clean.Category);
        Assert.Empty(clean.Seasons);
        Assert.Equal(1, clean.Formality);
    }

    [Fact]
    public async Task Gemini_analyzer_parses_valid_json()
    {
        var handler = new StubHttpHandler("""
            {"candidates":[{"content":{"parts":[{"text":"{\"name\":\"Camisa\",\"category\":\"top\",\"colors\":[{\"name\":\"beige\",\"hex\":\"#D8C3A5\"}],\"seasons\":[\"summer\"],\"formality\":3,\"styleTags\":[\"minimalista\"],\"confidence\":0.9}"}]}}]}
            """);
        var analyzer = new GeminiGarmentAnalyzer(new HttpClient(handler), Options.Create(new AiOptions { ApiKey = "test" }));

        var result = await analyzer.AnalyzeAsync([1, 2, 3], "image/jpeg", default);
        Assert.Equal("Camisa", result.Name);
        Assert.Equal(0.9, result.Confidence);
    }

    [Fact]
    public async Task Gemini_analyzer_signals_no_garment_instead_of_empty_analysis()
    {
        var handler = new StubHttpHandler(
            """{"candidates":[{"content":{"parts":[{"text":"{\"error\":\"no_garment\"}"}]}}]}""");
        var analyzer = new GeminiGarmentAnalyzer(
            new HttpClient(handler),
            Options.Create(new AiOptions { ApiKey = "test" }));

        await Assert.ThrowsAsync<NoGarmentDetectedException>(
            () => analyzer.AnalyzeAsync([1, 2, 3], "image/jpeg", default));
    }

    [Fact]
    public async Task Gemini_analyzer_returns_empty_analysis_on_garbage()
    {
        var handler = new StubHttpHandler("""{"candidates":[{"content":{"parts":[{"text":"no soy json"}]}}]}""");
        var analyzer = new GeminiGarmentAnalyzer(new HttpClient(handler), Options.Create(new AiOptions { ApiKey = "test" }));
        var result = await analyzer.AnalyzeAsync([1, 2, 3], "image/jpeg", default);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public async Task Gemini_analyzer_returns_empty_analysis_when_transport_fails()
    {
        var analyzer = new GeminiGarmentAnalyzer(
            new HttpClient(new ThrowingHttpHandler()),
            Options.Create(new AiOptions { ApiKey = "test" }));

        var result = await analyzer.AnalyzeAsync([1, 2, 3], "image/jpeg", default);
        Assert.Equal(0, result.Confidence);
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("El proveedor de IA no está disponible.");
    }
}
