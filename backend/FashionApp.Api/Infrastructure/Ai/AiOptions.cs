namespace FashionApp.Api.Infrastructure.Ai;

/// <summary>
/// Configuration for the garment analysis provider. Bound from the
/// <c>Ai</c> configuration section (environment variables <c>AI__*</c>).
/// </summary>
public sealed class AiOptions
{
    public string Provider { get; set; } = "fake";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";

    /// <summary>
    /// Optional API base URL. Empty means the provider default:
    /// Google Generative Language for <c>gemini</c> and
    /// <c>https://opencode.ai/inference</c> for <c>opencode</c>.
    /// </summary>
    public string BaseUrl { get; set; } = "";
}
