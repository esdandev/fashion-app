using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    public string? Token { get; init; }
    public bool Demo { get; init; }
    public string UploadsPath { get; } =
        Path.Combine(Path.GetTempPath(), "fashion-tests", Guid.NewGuid().ToString("N"));
    public string DbPath => Path.Combine(UploadsPath, "test.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(UploadsPath);
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:Token"] = Token,
                ["Demo:Enabled"] = Demo.ToString(),
                ["Digest:Provider"] = "fake",
                ["Storage:Root"] = UploadsPath,
                ["ConnectionStrings:Default"] = $"Data Source={DbPath}"
            });
        });
    }
}
