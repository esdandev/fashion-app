using System.Text;
using System.Text.Json;
using FashionApp.Api.Features.Outfits;
using Microsoft.Extensions.Options;

namespace FashionApp.Api.Infrastructure.Ai;

/// <summary>
/// Calls the Gemini text endpoint with the outfit prompt and parses the model
/// answer as strict JSON. Any transport error, non-success status or malformed
/// payload degrades to an empty list: the suggestion service then retries once
/// and falls back to <see cref="RuleBasedOutfitSuggester"/>. Drafts are still
/// untrusted and must pass <see cref="OutfitSuggestionValidator"/>.
/// </summary>
public sealed class GeminiOutfitSuggester : IOutfitSuggester
{
    private const string PromptResource =
        "FashionApp.Api.Infrastructure.Ai.Prompts.outfit-suggestions.md";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly Lazy<string> PromptText = new(LoadPrompt);

    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly string _prompt = PromptText.Value;

    public GeminiOutfitSuggester(HttpClient httpClient, IOptions<AiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.Timeout = RequestTimeout;
    }

    public async Task<IReadOnlyList<OutfitDraft>> SuggestAsync(
        OutfitSuggestionContext context,
        CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = BuildPrompt(_prompt, context) }
                        }
                    }
                },
                generationConfig = new
                {
                    responseMimeType = "application/json",
                    temperature = 0.4
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
                return [];

            return ParseDrafts(answer, context);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }
    }

    internal static string BuildPrompt(string prompt, OutfitSuggestionContext context)
    {
        var wardrobe = (context.Wardrobe ?? [])
            .Select(garment => new
            {
                id = garment.Id,
                category = garment.Category,
                color = garment.ColorName,
                formality = garment.Formality,
                seasons = garment.Seasons,
                styleTags = garment.StyleTags
            });

        var wardrobeJson = JsonSerializer.Serialize(wardrobe, SerializerOptions);
        var occasion = string.IsNullOrWhiteSpace(context.Occasion)
            ? "(sin preferencia)"
            : context.Occasion.Trim();

        return prompt
            .Replace("{{OCCASION}}", occasion, StringComparison.Ordinal)
            .Replace("{{WEATHER}}", context.Weather, StringComparison.Ordinal)
            .Replace("{{WARDROBE}}", wardrobeJson, StringComparison.Ordinal);
    }

    private static string LoadPrompt()
    {
        var assembly = typeof(GeminiOutfitSuggester).Assembly;
        using var stream = assembly.GetManifestResourceStream(PromptResource)
            ?? throw new InvalidOperationException(
                "No se encontró el prompt embebido 'outfit-suggestions.md'.");
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

    internal static IReadOnlyList<OutfitDraft> ParseDrafts(
        string answer,
        OutfitSuggestionContext context)
    {
        try
        {
            using var document = JsonDocument.Parse(TrimCodeFence(answer));
            var root = document.RootElement;

            if (!TryGetProperty(root, "outfits", out var outfits)
                || outfits.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var drafts = new List<OutfitDraft>();
            foreach (var item in outfits.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var ids = ReadItemIds(item);
                if (ids.Count == 0)
                    continue;

                var name = GetString(item, "name")?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                    name = "Look sugerido";

                var occasion = GetString(item, "occasion")?.Trim();
                if (string.IsNullOrWhiteSpace(occasion))
                    occasion = context.Occasion;

                var rationale = GetString(item, "rationale")?.Trim() ?? "";
                drafts.Add(new OutfitDraft(name, ids.ToArray(), occasion, rationale));
            }

            return drafts;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<Guid> ReadItemIds(JsonElement item)
    {
        var ids = new List<Guid>();
        if (!TryGetProperty(item, "itemIds", out var element)
            || element.ValueKind != JsonValueKind.Array)
        {
            return ids;
        }

        foreach (var id in element.EnumerateArray())
        {
            if (id.ValueKind == JsonValueKind.String
                && Guid.TryParse(id.GetString(), out var parsed)
                && parsed != Guid.Empty)
            {
                // Repeated ids are preserved on purpose: the validator must see
                // the repetition and discard the whole proposal, per the spec's
                // "no repeated garment" rule. Repairing it here would hide an
                // invalid model answer.
                ids.Add(parsed);
            }
        }

        return ids;
    }

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

    private static string? GetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

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
}
