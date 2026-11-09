using System.Text;
using System.Text.Json;
using FashionApp.Api.Features.Digest;
using FashionApp.Api.Infrastructure.Feeds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FashionApp.Api.Infrastructure.Ai;

/// <summary>
/// OpenAI Chat Completions digest summarizer. Shares the prompt, the source
/// allow-list and the parser with the Gemini path.
/// </summary>
public sealed class OpenAiDigestSummarizer : IDigestSummarizer
{
    private const string PromptResource =
        "FashionApp.Api.Infrastructure.Ai.Prompts.digest-summary.md";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private static readonly Lazy<string> PromptText = new(LoadPrompt);

    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly ILogger<OpenAiDigestSummarizer> _logger;
    private readonly string _prompt = PromptText.Value;

    public OpenAiDigestSummarizer(HttpClient httpClient, IOptions<AiOptions> options)
        : this(httpClient, options, NullLogger<OpenAiDigestSummarizer>.Instance)
    {
    }

    [ActivatorUtilitiesConstructor]
    public OpenAiDigestSummarizer(
        HttpClient httpClient,
        IOptions<AiOptions> options,
        ILogger<OpenAiDigestSummarizer> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
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
                GeminiDigestSummarizer.BuildEntriesJson(entries),
                StringComparison.Ordinal);

            var payload = new
            {
                model = AiRequestTargetFactory.ResolveModel(
                    _options.Provider,
                    _options.Model),
                messages = new object[]
                {
                    new { role = "user", content = prompt }
                },
                response_format = new { type = "json_object" },
                temperature = 0.3
            };

            using var request = OpenAiChat.CreateRequest(_options, payload);
            using var response = await _httpClient.SendAsync(request, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct)
                    .ConfigureAwait(false);
                _logger.LogWarning(
                    "El proveedor OpenAI-compatible respondió {Status} al resumir el digest: {Body}",
                    (int)response.StatusCode,
                    Truncate(errorBody));
                return new DigestSummary(null, []);
            }

            var body = await response.Content.ReadAsStringAsync(ct)
                .ConfigureAwait(false);
            var answer = OpenAiChat.ExtractContent(body);
            if (string.IsNullOrWhiteSpace(answer))
                return new DigestSummary(null, []);

            return GeminiDigestSummarizer.ParseSummary(answer, entries);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Falló el resumen del digest con el proveedor OpenAI-compatible.");
            return new DigestSummary(null, []);
        }
    }

    private static string LoadPrompt()
    {
        var assembly = typeof(OpenAiDigestSummarizer).Assembly;
        using var stream = assembly.GetManifestResourceStream(PromptResource)
            ?? throw new InvalidOperationException(
                "No se encontró el prompt embebido 'digest-summary.md'.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string Truncate(string value) =>
        value.Length <= 1000 ? value : value[..1000];
}
