using System.Net;
using FashionApp.Api.Infrastructure;

public sealed class SpaHostingTests
{
    private const string HashedAsset = "assets/app-3f9c1d.js";

    [Fact]
    public async Task Root_serves_the_spa_when_wwwroot_exists()
    {
        using var factory = new TestAppFactory { WithSpa = true };
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType!.ToString());
        Assert.Contains("Fashion App", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_spa_route_serves_the_spa()
    {
        using var factory = new TestAppFactory { WithSpa = true };
        var client = factory.CreateClient();

        var response = await client.GetAsync("/guardarropa");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType!.ToString());
        Assert.Contains("Fashion App", await response.Content.ReadAsStringAsync());
        Assert.Equal("no-cache", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Unknown_api_route_is_not_swallowed_by_the_spa()
    {
        using var factory = new TestAppFactory { WithSpa = true };
        var response = await factory.CreateClient().GetAsync("/api/no-existe");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("text/html", response.Content.Headers.ContentType?.ToString() ?? "");
    }

    [Fact]
    public async Task Api_health_keeps_priority_over_the_spa()
    {
        using var factory = new TestAppFactory { WithSpa = true };
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("application/json", response.Content.Headers.ContentType!.ToString());
        Assert.Contains("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Media_route_keeps_priority_over_the_spa()
    {
        using var factory = new TestAppFactory { WithSpa = true };
        var client = factory.CreateClient();
        var fileName = new string('a', 64) + ".webp";

        var response = await client.GetAsync($"/media/{fileName}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("text/html", response.Content.Headers.ContentType?.ToString() ?? "");
    }

    [Fact]
    public async Task Index_html_is_served_with_no_cache()
    {
        using var factory = new TestAppFactory { WithSpa = true };
        var client = factory.CreateClient();

        var root = await client.GetAsync("/");
        var direct = await client.GetAsync("/index.html");

        Assert.Equal("no-cache", root.Headers.CacheControl!.ToString());
        Assert.Equal("no-cache", direct.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Hashed_assets_are_cached_immutably()
    {
        using var factory = new TestAppFactory { WithSpa = true };
        var assetPath = Path.Combine(factory.SpaRoot, HashedAsset.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
        await File.WriteAllTextAsync(assetPath, "console.log('spa');");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/" + HashedAsset);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "public, max-age=31536000, immutable",
            response.Headers.CacheControl!.ToString());
        Assert.Contains("javascript", response.Content.Headers.ContentType!.ToString());
        Assert.Contains("console.log", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Fingerprinted_file_outside_assets_is_cached_immutably()
    {
        using var factory = new TestAppFactory { WithSpa = true };
        await WriteSpaFileAsync(factory, "main-B0a1C2d3.js", "console.log('spa');");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/main-B0a1C2d3.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            SpaStaticFiles.ImmutableCacheControl,
            response.Headers.CacheControl!.ToString());
    }

    [Theory]
    [InlineData("favicon.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")]
    [InlineData("demo-note.webp", "not-a-real-webp")]
    public async Task Non_fingerprinted_public_files_get_a_short_cache(
        string fileName,
        string content)
    {
        using var factory = new TestAppFactory { WithSpa = true };
        await WriteSpaFileAsync(factory, fileName, content);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/" + fileName);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            SpaStaticFiles.ShortCacheControl,
            response.Headers.CacheControl!.ToString());
    }

    private static async Task WriteSpaFileAsync(
        TestAppFactory factory,
        string relativePath,
        string content)
    {
        var path = Path.Combine(
            factory.SpaRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }
}
