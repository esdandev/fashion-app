using System.Net.Http.Headers;

namespace FashionApp.Api.Infrastructure.Ai;

/// <summary>
/// Builds the Gemini-compatible <c>generateContent</c> request for the selected
/// provider. Google authenticates with a <c>?key=</c> query parameter; OpenCode
/// exposes the same Gemini payload at
/// <c>https://opencode.ai/inference/google/v1beta/models/{model}:generateContent</c>
/// and authenticates with a bearer token.
/// </summary>
public sealed record AiRequestTarget(Uri Uri, bool UseBearer);

public static class AiRequestTargetFactory
{
    public const string GoogleBaseUrl = "https://generativelanguage.googleapis.com";
    public const string OpenCodeBaseUrl = "https://opencode.ai/inference";

    /// <summary>
    /// Provider-aware default: Google uses <c>gemini-3.8-flash</c>; OpenCode
    /// exposes the multimodal model as <c>gemini-4-argon</c>.
    /// </summary>
    public static string ResolveModel(string? provider, string? model)
    {
        if (!string.IsNullOrWhiteSpace(model))
            return model;

        return string.Equals(
            provider?.Trim(),
            "opencode",
            StringComparison.OrdinalIgnoreCase)
            ? "gemini-4-argon"
            : "gemini-3.8-flash";
    }

    public static AiRequestTarget For(AiOptions options)
    {
        var provider = (options.Provider ?? "").Trim().ToLowerInvariant();
        var model = Uri.EscapeDataString(
            ResolveModel(options.Provider, options.Model));
        var configuredBase = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? null
            : options.BaseUrl.TrimEnd('/');

        if (provider == "opencode")
        {
            var baseUrl = configuredBase ?? OpenCodeBaseUrl;
            return new AiRequestTarget(
                new Uri($"{baseUrl}/google/v1beta/models/{model}:generateContent"),
                UseBearer: true);
        }

        var googleBase = configuredBase ?? GoogleBaseUrl;
        return new AiRequestTarget(
            new Uri(
                $"{googleBase}/v1beta/models/{model}:generateContent" +
                $"?key={Uri.EscapeDataString(options.ApiKey)}"),
            UseBearer: false);
    }

    /// <summary>
    /// Creates the POST with the provider's authentication already applied.
    /// The caller owns the returned request and must dispose it.
    /// </summary>
    public static HttpRequestMessage CreateRequest(AiOptions options, HttpContent content)
    {
        var target = For(options);
        var request = new HttpRequestMessage(HttpMethod.Post, target.Uri)
        {
            Content = content
        };

        if (target.UseBearer)
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        return request;
    }
}
