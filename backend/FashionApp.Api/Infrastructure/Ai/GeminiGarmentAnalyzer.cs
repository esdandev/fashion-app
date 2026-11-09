using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FashionApp.Api.Infrastructure.Ai;

/// <summary>
/// Calls the Gemini multimodal endpoint with the garment prompt and parses the
/// model answer as strict JSON. Any transport error, non-success status or
/// malformed payload degrades to <see cref="GarmentAnalysis.Empty()"/> so the
/// upload flow can keep the photo with <c>Confidence = 0</c>.
/// </summary>
public sealed class GeminiGarmentAnalyzer : IGarmentAnalyzer
{
    private const string PromptResource =
        "FashionApp.Api.Infrastructure.Ai.Prompts.garment-analysis.md";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly Lazy<string> PromptText = new(LoadPrompt);

    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly ILogger<GeminiGarmentAnalyzer> _logger;
    private readonly string _prompt = PromptText.Value;

    public GeminiGarmentAnalyzer(HttpClient httpClient, IOptions<AiOptions> options)
        : this(httpClient, options, NullLogger<GeminiGarmentAnalyzer>.Instance)
    {
    }

    [ActivatorUtilitiesConstructor]
    public GeminiGarmentAnalyzer(
        HttpClient httpClient,
        IOptions<AiOptions> options,
        ILogger<GeminiGarmentAnalyzer> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.Timeout = RequestTimeout;
    }

    public async Task<GarmentAnalysis> AnalyzeAsync(
        byte[] image,
        string contentType,
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
                            new { text = _prompt },
                            new
                            {
                                inline_data = new
                                {
                                    mime_type = contentType,
                                    data = Convert.ToBase64String(image)
                                }
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    responseMimeType = "application/json",
                    temperature = 0
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

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content
                    .ReadAsStringAsync(ct)
                    .ConfigureAwait(false);
                _logger.LogWarning(
                    "Gemini respondió {Status} para el modelo {Model}: {Body}. " +
                    "Revisá AI__APIKEY y AI__MODEL.",
                    (int)response.StatusCode,
                    _options.Model,
                    Truncate(errorBody));
                return GarmentAnalysis.Empty();
            }

            var body = await response.Content
                .ReadAsStringAsync(ct)
                .ConfigureAwait(false);

            var analysisText = ExtractAnswerText(body);
            if (string.IsNullOrWhiteSpace(analysisText))
            {
                _logger.LogWarning(
                    "Gemini devolvió una respuesta sin texto para el modelo {Model}.",
                    _options.Model);
                return GarmentAnalysis.Empty();
            }

            var parsed = ParseAnalysis(analysisText);
            if (parsed is null || !GarmentAnalysisValidator.TryNormalize(parsed, out var normalized))
            {
                _logger.LogWarning(
                    "No se pudo validar la respuesta de Gemini: {Body}",
                    Truncate(analysisText));
                return GarmentAnalysis.Empty();
            }

            return normalized;
        }
        catch (NoGarmentDetectedException)
        {
            // An explicit "no garment" answer is a product signal, not a provider
            // failure: the endpoints map it to a 422 instead of saving "Prenda".
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A cancelled request must not be turned into a saved garment.
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Falló la llamada a Gemini con el modelo {Model}.",
                _options.Model);
            return GarmentAnalysis.Empty();
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 1000 ? value : value[..1000];

    private static string LoadPrompt()
    {
        var assembly = typeof(GeminiGarmentAnalyzer).Assembly;
        using var stream = assembly.GetManifestResourceStream(PromptResource)
            ?? throw new InvalidOperationException(
                "No se encontró el prompt embebido 'garment-analysis.md'.");
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

    private static GarmentAnalysis? ParseAnalysis(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (FindProperty(root, "error") is { } error)
            {
                if (error.ValueKind == JsonValueKind.String
                    && string.Equals(
                        error.GetString(),
                        "no_garment",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new NoGarmentDetectedException();
                }

                return null;
            }

            return new GarmentAnalysis(
                Name: GetString(root, "name") ?? "",
                Category: GetString(root, "category") ?? "",
                Subcategory: GetString(root, "subcategory"),
                Colors: GetStringArray(root, "colors"),
                Pattern: GetString(root, "pattern"),
                Seasons: GetStringArray(root, "seasons"),
                Formality: GetInt(root, "formality") ?? 1,
                Material: GetString(root, "material"),
                StyleTags: GetStringArray(root, "styleTags"),
                Confidence: GetDouble(root, "confidence") ?? 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Accepts both <c>"colors": ["beige"]</c> and the contract shape
    /// <c>"colors": [{"name": "beige", "hex": "#D8C3A5"}]</c>.
    /// </summary>
    private static string[] GetStringArray(JsonElement element, string name)
    {
        var property = FindProperty(element, name);
        if (property is not { ValueKind: JsonValueKind.Array } array)
            return [];

        var result = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value);
            }
            else if (item.ValueKind == JsonValueKind.Object
                && GetString(item, "name") is { Length: > 0 } objectName)
            {
                result.Add(objectName);
            }
        }

        return [.. result];
    }

    private static string? GetString(JsonElement element, string name)
    {
        var property = FindProperty(element, name);
        return property is { ValueKind: JsonValueKind.String }
            ? property.Value.GetString()
            : null;
    }

    private static int? GetInt(JsonElement element, string name)
    {
        var property = FindProperty(element, name);
        if (property is null)
            return null;

        if (property.Value.ValueKind == JsonValueKind.Number
            && property.Value.TryGetDouble(out var number))
        {
            return (int)Math.Round(number, MidpointRounding.AwayFromZero);
        }

        if (property.Value.ValueKind == JsonValueKind.String
            && double.TryParse(
                property.Value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return (int)Math.Round(parsed, MidpointRounding.AwayFromZero);
        }

        return null;
    }

    private static double? GetDouble(JsonElement element, string name)
    {
        var property = FindProperty(element, name);
        if (property is null)
            return null;

        if (property.Value.ValueKind == JsonValueKind.Number
            && property.Value.TryGetDouble(out var number))
        {
            return number;
        }

        if (property.Value.ValueKind == JsonValueKind.String
            && double.TryParse(
                property.Value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static JsonElement? FindProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        return null;
    }
}
