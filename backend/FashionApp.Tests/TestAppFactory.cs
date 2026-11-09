using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    public string? Token { get; init; }
    public bool Demo { get; init; }
    public string? AdminSecret { get; init; }
    public bool WithSpa { get; init; }
    public string UploadsPath { get; } =
        Path.Combine(Path.GetTempPath(), "fashion-tests", Guid.NewGuid().ToString("N"));
    public string DbPath => Path.Combine(UploadsPath, "test.db");
    public string SpaRoot => Path.Combine(UploadsPath, "wwwroot");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(UploadsPath);
        builder.UseEnvironment("Testing");

        if (WithSpa)
        {
            Directory.CreateDirectory(SpaRoot);
            File.WriteAllText(
                Path.Combine(SpaRoot, "index.html"),
                "<!doctype html><html><head><title>Fashion App</title></head>" +
                "<body><div id=\"root\"></div></body></html>");
            // The API has no wwwroot of its own in the repository; the compiled
            // SPA is copied there by the production container.
            builder.UseWebRoot(SpaRoot);
        }

        // Program picks the feed provider before WebApplicationFactory's
        // in-memory configuration is attached, so the test default goes
        // through the host settings instead.
        builder.UseSetting("Digest:Provider", "fake");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:Token"] = Token,
                ["Demo:Enabled"] = Demo.ToString(),
                ["Digest:Provider"] = "fake",
                ["Digest:AdminSecret"] = AdminSecret,
                // The digest job must never run behind a test's back: tests
                // that need a digest call the refresh endpoint or seed it.
                ["Digest:GenerateOnStartup"] = "false",
                // A fake feed keeps refresh deterministic without network.
                ["Digest:Feeds:0:Name"] = "Feed de prueba",
                ["Digest:Feeds:0:Url"] = "fake://digest",
                ["Storage:Root"] = UploadsPath,
                ["ConnectionStrings:Default"] = $"Data Source={DbPath}"
            });
        });
    }
}
