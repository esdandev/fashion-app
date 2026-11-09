using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FashionApp.Api.Infrastructure.Ai;
using Microsoft.Extensions.Options;

// Regression coverage for the OpenAI-compatible adapter (DeepSeek on OpenCode).
namespace FashionApp.Tests;

public sealed class OpenAiAdapterTests
{
    [Fact]
    public void Picks_openai_for_non_gemini_models_on_opencode()
    {
        Assert.Equal(
            AiApiFamily.OpenAi,
            AiApiFamilyResolver.Resolve("opencode", "deepseek-v4.1-flash"));
        Assert.Equal(
            AiApiFamily.Gemini,
            AiApiFamilyResolver.Resolve("opencode", "gemini-4-argon"));
        Assert.Equal(
            AiApiFamily.OpenAi,
            AiApiFamilyResolver.Resolve("openai", "gpt-6-luna"));
    }

    [Fact]
    public void Builds_the_opencode_chat_completions_url()
    {
        var options = new AiOptions
        {
            Provider = "opencode",
            ApiKey = "k",
            Model = "deepseek-v4.1-flash"
        };

        Assert.Equal(
            "https://opencode.ai/inference/openai/v1/chat/completions",
            OpenAiChat.ChatCompletionsUri(options).ToString());
    }

    [Fact]
    public async Task Garment_analyzer_sends_the_image_and_parses_the_answer()
    {
        const string analysis =
            """{"name":"Camisa de lino","category":"top","subcategory":"camisa","colors":["beige"],"pattern":"liso","seasons":["spring"],"formality":3,"material":"lino","styleTags":["minimalista"],"confidence":0.9}""";
        var envelope = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = analysis } }
            }
        });
        var handler = new CapturingHandler(envelope);
        var options = new AiOptions
        {
            Provider = "opencode",
            ApiKey = "opencode-key",
            Model = "deepseek-v4.1-flash"
        };
        var analyzer = new OpenAiGarmentAnalyzer(
            new HttpClient(handler),
            Options.Create(options));

        var result = await analyzer.AnalyzeAsync([1, 2, 3], "image/jpeg", default);

        Assert.Equal("Camisa de lino", result.Name);
        Assert.Equal(0.9, result.Confidence);
        Assert.Equal(
            "https://opencode.ai/inference/openai/v1/chat/completions",
            handler.RequestUri!.ToString());
        Assert.Equal(
            new AuthenticationHeaderValue("Bearer", "opencode-key"),
            handler.Authorization);
        Assert.Contains("deepseek-v4.1-flash", handler.Body);
        Assert.Contains("image_url", handler.Body);
        Assert.Contains("data:image/jpeg;base64,", handler.Body);
    }

    private sealed class CapturingHandler(string body) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            Body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
