using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FashionApp.Api.Infrastructure.Ai;

/// <summary>
/// OpenAI Chat Completions implementation for multimodal providers such as
/// DeepSeek V4.1 Flash on OpenCode. The image travels as a data URL, so no
/// public image hosting is required.
/// </summary>
public sealed class OpenAiGarmentAnalyzer : IGarmentAnalyzer
{
    private const string PromptResource =
        "FashionApp.Api.Infrastructure.Ai.Prompts.garment-analysis.md";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private static readonly Lazy<string> PromptText = new(LoadPrompt);

    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly ILogger<OpenAiGarmentAnalyzer> _logger;
    private readonly string _prompt = PromptText.Value;

    public OpenAiGarmentAnalyzer(HttpClient httpClient, IOptions<AiOptions> options)
        : this(httpClient, options, NullLogger<OpenAiGarmentAnalyzer>.Instance)
    {
    }

    [ActivatorUtilitiesConstructor]
    public OpenAiGarmentAnalyzer(
        HttpClient httpClient,
        IOptions<AiOptions> options,
        ILogger<OpenAiGarmentAnalyzer> logger)
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
                model = AiRequestTargetFactory.ResolveModel(
                    _options.Provider,
                    _options.Model),
                messages = new object[]
                {
                    new { role = "system", content = _prompt },
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = "Analizá la prenda de la imagen." },
                            new
                            {
                                type = "image_url",
                                image_url = new
                                {
                                    url = $"data:{contentType};base64,{Convert.ToBase64String(image)}"
                                }
                            }
                        }
                    }
                },
                response_format = new { type = "json_object" },
                temperature = 0
            };

            using var request = OpenAiChat.CreateRequest(_options, payload);
            using var response = await _httpClient.SendAsync(request, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct)
                    .ConfigureAwait(false);
                _logger.LogWarning(
                    "El proveedor OpenAI-compatible respondió {Status} para el modelo {Model}: {Body}",
                    (int)response.StatusCode,
                    AiRequestTargetFactory.ResolveModel(_options.Provider, _options.Model),
                    Truncate(errorBody));
                return GarmentAnalysis.Empty();
            }

            var body = await response.Content.ReadAsStringAsync(ct)
                .ConfigureAwait(false);
            var answer = OpenAiChat.ExtractContent(body);
            if (string.IsNullOrWhiteSpace(answer))
                return GarmentAnalysis.Empty();

            var parsed = GeminiGarmentAnalyzer.ParseAnalysis(answer);
            if (parsed is null
                || !GarmentAnalysisValidator.TryNormalize(parsed, out var normalized))
            {
                _logger.LogWarning(
                    "No se pudo validar la respuesta del proveedor: {Body}",
                    Truncate(answer));
                return GarmentAnalysis.Empty();
            }

            return normalized;
        }
        catch (NoGarmentDetectedException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Falló la llamada al proveedor OpenAI-compatible con el modelo {Model}.",
                AiRequestTargetFactory.ResolveModel(_options.Provider, _options.Model));
            return GarmentAnalysis.Empty();
        }
    }

    private static string LoadPrompt()
    {
        var assembly = typeof(OpenAiGarmentAnalyzer).Assembly;
        using var stream = assembly.GetManifestResourceStream(PromptResource)
            ?? throw new InvalidOperationException(
                "No se encontró el prompt embebido 'garment-analysis.md'.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string Truncate(string value) =>
        value.Length <= 1000 ? value : value[..1000];
}
