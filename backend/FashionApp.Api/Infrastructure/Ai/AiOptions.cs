namespace FashionApp.Api.Infrastructure.Ai;

/// <summary>
/// Configuration for the garment analysis provider. Bound from the
/// <c>Ai</c> configuration section (environment variables <c>AI__*</c>).
/// </summary>
public sealed class AiOptions
{
    public string Provider { get; set; } = "fake";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemini-3.8-flash";
}
