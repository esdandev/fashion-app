using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FashionApp.Api.Infrastructure.Ai;

public enum AiApiFamily
{
    Gemini,
    OpenAi
}

/// <summary>
/// Picks the API family from the provider and model. OpenCode serves Gemini
/// models through the Google-compatible endpoint and the rest (DeepSeek, GLM,
/// Qwen, Kimi, ...) through OpenAI Chat Completions.
/// </summary>
public static class AiApiFamilyResolver
{
    public static AiApiFamily Resolve(string? provider, string? model)
    {
        var normalizedProvider = (provider ?? "").Trim().ToLowerInvariant();
        if (normalizedProvider == "openai")
            return AiApiFamily.OpenAi;

        if (normalizedProvider == "opencode")
        {
            var normalizedModel = (model ?? "").Trim();
            return normalizedModel.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase)
                ? AiApiFamily.Gemini
                : AiApiFamily.OpenAi;
        }

        return AiApiFamily.Gemini;
    }
}

/// <summary>
/// Builds OpenAI Chat Completions requests and reads the assistant message.
/// Works with OpenCode's gateway and with any OpenAI-compatible endpoint.
/// </summary>
public static class OpenAiChat
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public static Uri ChatCompletionsUri(AiOptions options)
    {
        var provider = (options.Provider ?? "").Trim().ToLowerInvariant();
        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? provider == "openai"
                ? "https://api.openai.com/v1"
                : "https://opencode.ai/inference/openai/v1"
            : options.BaseUrl.TrimEnd('/');

        return new Uri($"{baseUrl}/chat/completions");
    }

    public static HttpRequestMessage CreateRequest(AiOptions options, object payload)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            ChatCompletionsUri(options))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, SerializerOptions),
                Encoding.UTF8,
                "application/json")
        };

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", options.ApiKey);

        return request;
    }

    /// <summary>
    /// Reads <c>choices[0].message.content</c>. Some providers return content
    /// as an array of parts; the text parts are joined.
    /// </summary>
    public static string? ExtractContent(string body)
    {
        using var document = JsonDocument.Parse(body);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content");

        if (content.ValueKind == JsonValueKind.String)
            return content.GetString();

        if (content.ValueKind != JsonValueKind.Array)
            return null;

        var parts = new List<string>();
        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.Object
                && part.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String)
            {
                parts.Add(text.GetString() ?? "");
            }
        }

        return string.Join("", parts);
    }
}
