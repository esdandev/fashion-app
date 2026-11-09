using System.Text;
using System.Text.Json;
using FashionApp.Api.Features.Outfits;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FashionApp.Api.Infrastructure.Ai;

/// <summary>
/// OpenAI Chat Completions outfit suggester for providers such as DeepSeek on
/// OpenCode. The prompt and the draft parser are shared with the Gemini path,
/// so both providers obey the same rules and JSON contract.
/// </summary>
public sealed class OpenAiOutfitSuggester : IOutfitSuggester
{
    private const string PromptResource =
        "FashionApp.Api.Infrastructure.Ai.Prompts.outfit-suggestions.md";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private static readonly Lazy<string> PromptText = new(LoadPrompt);

    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly ILogger<OpenAiOutfitSuggester> _logger;
    private readonly string _prompt = PromptText.Value;

    public OpenAiOutfitSuggester(HttpClient httpClient, IOptions<AiOptions> options)
        : this(httpClient, options, NullLogger<OpenAiOutfitSuggester>.Instance)
    {
    }

    [ActivatorUtilitiesConstructor]
    public OpenAiOutfitSuggester(
        HttpClient httpClient,
        IOptions<AiOptions> options,
        ILogger<OpenAiOutfitSuggester> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
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
                model = AiRequestTargetFactory.ResolveModel(
                    _options.Provider,
                    _options.Model),
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = GeminiOutfitSuggester.BuildPrompt(_prompt, context)
                    }
                },
                response_format = new { type = "json_object" },
                temperature = 0.4
            };

            using var request = OpenAiChat.CreateRequest(_options, payload);
            using var response = await _httpClient.SendAsync(request, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct)
                    .ConfigureAwait(false);
                _logger.LogWarning(
                    "El proveedor OpenAI-compatible respondió {Status} al sugerir outfits: {Body}",
                    (int)response.StatusCode,
                    Truncate(errorBody));
                return [];
            }

            var body = await response.Content.ReadAsStringAsync(ct)
                .ConfigureAwait(false);
            var answer = OpenAiChat.ExtractContent(body);
            if (string.IsNullOrWhiteSpace(answer))
                return [];

            return GeminiOutfitSuggester.ParseDrafts(answer, context);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Falló la sugerencia de outfits con el proveedor OpenAI-compatible.");
            return [];
        }
    }

    private static string LoadPrompt()
    {
        var assembly = typeof(OpenAiOutfitSuggester).Assembly;
        using var stream = assembly.GetManifestResourceStream(PromptResource)
            ?? throw new InvalidOperationException(
                "No se encontró el prompt embebido 'outfit-suggestions.md'.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string Truncate(string value) =>
        value.Length <= 1000 ? value : value[..1000];
}
