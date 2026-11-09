using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace FashionApp.Tests;

public sealed class AiStatusTests
{
    [Fact]
    public async Task Reports_the_configured_provider_model_and_key_presence()
    {
        using var factory = new TestAppFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Ai:Provider"] = "gemini",
                    ["Ai:ApiKey"] = "clave-de-prueba",
                    ["Ai:Model"] = "modelo-de-prueba"
                })));

        var body = await factory.CreateClient()
            .GetFromJsonAsync<JsonElement>("/api/ai/status");

        Assert.Equal("gemini", body.GetProperty("provider").GetString());
        Assert.Equal("modelo-de-prueba", body.GetProperty("model").GetString());
        Assert.True(body.GetProperty("keyConfigured").GetBoolean());
    }
}
