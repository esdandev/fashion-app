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
}
