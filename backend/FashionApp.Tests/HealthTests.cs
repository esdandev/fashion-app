using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

public sealed class HealthTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task Health_returns_ok_and_utc()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.True(DateTimeOffset.TryParse(body.GetProperty("utc").GetString(), out _));
    }
}
