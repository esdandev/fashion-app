using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

public sealed class RateLimitingTests
{
    /// <summary>
    /// The README documents the environment-style names. The .NET environment
    /// provider maps the double underscore to a section separator, so these
    /// names must land on the keys the app reads.
    /// </summary>
    [Fact]
    public void Documented_environment_variable_names_bind_to_the_rate_limit_section()
    {
        const string enabledName = "RATELIMITING__ENABLED";
        const string permitName = "RATELIMITING__PERMITLIMIT";
        var previousEnabled = Environment.GetEnvironmentVariable(enabledName);
        var previousPermit = Environment.GetEnvironmentVariable(permitName);

        try
        {
            // Values chosen so a concurrently starting host cannot be affected:
            // disabling is also the Testing default, and the permit is unused
            // while the limiter is off.
            Environment.SetEnvironmentVariable(enabledName, "false");
            Environment.SetEnvironmentVariable(permitName, "1");

            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            Assert.Equal("false", configuration["RateLimiting:Enabled"]);
            Assert.Equal("1", configuration["RateLimiting:PermitLimit"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(enabledName, previousEnabled);
            Environment.SetEnvironmentVariable(permitName, previousPermit);
        }
    }

    [Fact]
    public async Task Rate_limit_falls_back_to_the_leftmost_forwarded_for()
    {
        using var factory = new TestAppFactory();
        using var limited = EnableLimiter(factory, permitLimit: 1);
        var client = limited.CreateClient();

        var first = new HttpRequestMessage(HttpMethod.Get, "/api/garments");
        first.Headers.Add("X-Forwarded-For", "203.0.113.40, 10.0.0.1");
        var sameVisitor = new HttpRequestMessage(HttpMethod.Get, "/api/garments");
        sameVisitor.Headers.Add("X-Forwarded-For", "203.0.113.40, 10.0.0.1");
        var otherVisitor = new HttpRequestMessage(HttpMethod.Get, "/api/garments");
        otherVisitor.Headers.Add("X-Forwarded-For", "203.0.113.41, 10.0.0.1");

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(first)).StatusCode);
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await client.SendAsync(sameVisitor)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.SendAsync(otherVisitor)).StatusCode);
    }

    private static WebApplicationFactory<Program> EnableLimiter(
        TestAppFactory factory,
        int permitLimit) =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:Enabled"] = "true",
                    ["RateLimiting:PermitLimit"] = permitLimit.ToString()
                })));
}
