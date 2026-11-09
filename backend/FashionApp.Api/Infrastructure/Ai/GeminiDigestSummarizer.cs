using System.Text;
using System.Text.Json;
using FashionApp.Api.Features.Digest;
using FashionApp.Api.Infrastructure.Feeds;
using Microsoft.Extensions.Options;

namespace FashionApp.Api.Infrastructure.Ai;

/// <summary>
/// Calls the Gemini text endpoint with the digest prompt and parses the answer
/// as strict JSON. Any transport error, non-success status or malformed payload
/// degrades to an empty summary: the generator then keeps the previous digest.
/// Drafts are untrusted and must still pass <see cref="DigestValidator"/>.
/// </summary>
public sealed class GeminiDigestSummarizer : IDigestSummarizer
{
    public const int MaxPromptEntries = 20;

    private const string PromptResource =
        "FashionApp.Api.Infrastructure.Ai.Prompts.digest-summary.md";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly Lazy<string> PromptText = new(LoadPrompt);

    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly string _prompt = PromptText.Value;

    public GeminiDigestSummarizer(HttpClient httpClient, IOptions<AiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.Timeout = RequestTimeout;
    }

    public async Task<DigestSummary> SummarizeAsync(
        IReadOnlyList<FeedEntry> entries,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entries);

        try
        {
            var prompt = _prompt.Replace(
                "{{ENTRIES}}",
                BuildEntriesJson(entries),
                StringComparison.Ordinal);

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[] { new { text = prompt } }
                    }
                },
                generationConfig = new
                {
                    responseMimeType = "application/json",
                    temperature = 0.3
                }
            };

            using var content = new StringContent(
                JsonSerializer.Serialize(payload, SerializerOptions),
                Encoding.UTF8,
                "application/json");

            using var request = AiRequestTargetFactory.CreateRequest(_options, content);
            using var response = await _httpClient
                .SendAsync(request, ct)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var body = await response.Content
                .ReadAsStringAsync(ct)
                .ConfigureAwait(false);

            var answer = ExtractAnswerText(body);
            if (string.IsNullOrWhiteSpace(answer))
                return Empty();

            return ParseSummary(answer, entries);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Empty();
        }
    }

    private static string BuildEntriesJson(IReadOnlyList<FeedEntry> entries)
    {
        var payload = entries
            .Take(MaxPromptEntries)
            .Select(entry => new
            {
                title = entry.Title,
                url = entry.Url,
                source = entry.Source,
                publishedAt = entry.PublishedAt?.ToUniversalTime().ToString("O"),
                summary = entry.Summary
            });

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    private static string LoadPrompt()
    {
        var assembly = typeof(GeminiDigestSummarizer).Assembly;
        using var stream = assembly.GetManifestResourceStream(PromptResource)
            ?? throw new InvalidOperationException(
                "No se encontró el prompt embebido 'digest-summary.md'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Reads <c>candidates[0].content.parts[0].text</c> from the Gemini
    /// envelope. Missing members throw and are handled by the caller.
    /// </summary>
    private static string? ExtractAnswerText(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();
    }

    /// <summary>
    /// Parses <c>{ "summary": "...", "items": [...] }</c>. Source, image and
    /// publication date are taken from the original feed entry with the same
    /// URL, never from the model. Unknown URLs are kept as drafts so the
    /// validator can drop them explicitly.
    /// </summary>
    private static DigestSummary ParseSummary(
        string answer,
        IReadOnlyList<FeedEntry> entries)
    {
        try
        {
            using var document = JsonDocument.Parse(TrimCodeFence(answer));
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !TryGetProperty(root, "items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return Empty();
            }

            var summary = GetString(root, "summary")?.Trim();
            if (string.IsNullOrWhiteSpace(summary))
                summary = null;

            var entriesByUrl = new Dictionary<string, FeedEntry>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.Url))
                    entriesByUrl.TryAdd(entry.Url.Trim(), entry);
            }

            var drafts = new List<DigestDraft>();
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var url = GetString(item, "url")?.Trim();
                if (string.IsNullOrWhiteSpace(url))
                    continue;

                entriesByUrl.TryGetValue(url, out var source);

                var title = GetString(item, "title")?.Trim();
                if (string.IsNullOrWhiteSpace(title))
                    title = source?.Title;
                if (string.IsNullOrWhiteSpace(title))
                    title = "Nota de moda";

                drafts.Add(new DigestDraft(
                    Title: title,
                    Url: url,
                    Source: source?.Source ?? "",
                    Summary: GetString(item, "summary")?.Trim() ?? "",
                    StyleTags: GetStringArray(item, "styleTags"),
                    ImageUrl: source?.ImageUrl,
                    PublishedAt: source?.PublishedAt));
            }

            return new DigestSummary(summary, drafts);
        }
        catch (JsonException)
        {
            return Empty();
        }
    }

    private static string[] GetStringArray(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;

            var value = item.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
                result.Add(value);
        }

        return [.. result];
    }

    private static string? GetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    /// <summary>
    /// Strips a markdown fence in case the provider ignores the JSON MIME type.
    /// </summary>
    private static string TrimCodeFence(string answer)
    {
        var trimmed = answer.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var firstLineEnd = trimmed.IndexOf('\n');
        if (firstLineEnd < 0)
            return trimmed;

        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence <= firstLineEnd)
            return trimmed;

        return trimmed[(firstLineEnd + 1)..lastFence].Trim();
    }

    private static bool TryGetProperty(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static DigestSummary Empty() => new(null, []);
}
