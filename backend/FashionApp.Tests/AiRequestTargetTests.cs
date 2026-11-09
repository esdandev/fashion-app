using System.Net.Http.Headers;
using FashionApp.Api.Infrastructure.Ai;

namespace FashionApp.Tests;

public sealed class AiRequestTargetTests
{
    [Fact]
    public void Google_uses_the_key_query_parameter()
    {
        var options = new AiOptions
        {
            Provider = "gemini",
            ApiKey = "google-key",
            Model = "gemini-3.8-flash"
        };

        var target = AiRequestTargetFactory.For(options);

        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent?key=google-key",
            target.Uri.ToString());
        Assert.False(target.UseBearer);
    }

    [Fact]
    public void OpenCode_uses_the_inference_gateway_and_bearer_auth()
    {
        var options = new AiOptions
        {
            Provider = "opencode",
            ApiKey = "opencode-key",
            Model = "gemini-4-argon"
        };

        var target = AiRequestTargetFactory.For(options);
        using var request = AiRequestTargetFactory.CreateRequest(
            options,
            new StringContent("{}"));

        Assert.Equal(
            "https://opencode.ai/inference/google/v1beta/models/gemini-4-argon:generateContent",
            target.Uri.ToString());
        Assert.True(target.UseBearer);
        Assert.Equal(
            new AuthenticationHeaderValue("Bearer", "opencode-key"),
            request.Headers.Authorization);
        Assert.DoesNotContain("key=", target.Uri.Query);
    }

    [Fact]
    public void Resolves_the_provider_default_model()
    {
        Assert.Equal(
            "gemini-3.8-flash",
            AiRequestTargetFactory.ResolveModel("gemini", ""));
        Assert.Equal(
            "gemini-4-argon",
            AiRequestTargetFactory.ResolveModel("opencode", ""));
        Assert.Equal(
            "custom-model",
            AiRequestTargetFactory.ResolveModel("opencode", "custom-model"));
    }

    [Fact]
    public void Custom_base_url_replaces_the_provider_default()
    {
        var options = new AiOptions
        {
            Provider = "opencode",
            ApiKey = "k",
            Model = "gemini-4-argon",
            BaseUrl = "https://gateway.example.com/"
        };

        var target = AiRequestTargetFactory.For(options);

        Assert.Equal(
            "https://gateway.example.com/google/v1beta/models/gemini-4-argon:generateContent",
            target.Uri.ToString());
    }
}
