using System.Net;
using System.Net.Http.Json;
using System.Text;
using FashionApp.Api.Features.Garments;
using FashionApp.Api.Infrastructure.Ai;
using FashionApp.Api.Infrastructure.Storage;
using ImageMagick;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public sealed class GarmentUploadTests : IDisposable
{
    private readonly TestAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Upload_creates_garment_and_writes_two_files()
    {
        // 2400x1200 forces both the 1600 px normalization and the 400 px thumb.
        var client = factory.CreateClient();
        using var content = TestImages.Multipart("camisa.jpg", TestImages.CreateJpeg(2400, 1200));
        var response = await client.PostAsync("/api/garments", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateGarmentResponse>();
        Assert.False(body!.Deduplicated);
        Assert.Equal(2, Directory.GetFiles(factory.UploadsPath, "*.webp").Length);

        var hash = Path.GetFileNameWithoutExtension(body.Garment.ImageUrl);
        Assert.Matches("^[a-f0-9]{64}$", hash);
        Assert.Equal($"{hash}-thumb.webp", Path.GetFileName(body.Garment.ThumbUrl));

        var originalPath = Path.Combine(
            factory.UploadsPath,
            Path.GetFileName(body.Garment.ImageUrl));
        var thumbPath = Path.Combine(
            factory.UploadsPath,
            Path.GetFileName(body.Garment.ThumbUrl));

        var originalBytes = await File.ReadAllBytesAsync(originalPath);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(originalBytes, 0, 4));
        Assert.Equal("WEBP", Encoding.ASCII.GetString(originalBytes, 8, 4));

        using var original = new MagickImage(originalPath);
        using var thumb = new MagickImage(thumbPath);

        Assert.Equal(MagickFormat.WebP, original.Format);
        Assert.Equal(MagickFormat.WebP, thumb.Format);
        Assert.True(Math.Max(original.Width, original.Height) <= 1600);
        Assert.True(Math.Max(thumb.Width, thumb.Height) <= 400);
        Assert.Equal(1600u, original.Width);
        Assert.Equal(800u, original.Height);
    }

    [Fact]
    public async Task Uploading_same_file_twice_returns_existing_id()
    {
        var client = factory.CreateClient();
        var bytes = TestImages.CreateJpeg(800, 600);
        var first = await client.PostAsync("/api/garments", TestImages.Multipart("a.jpg", bytes));
        var second = await client.PostAsync("/api/garments", TestImages.Multipart("b.jpg", bytes));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var a = await first.Content.ReadFromJsonAsync<CreateGarmentResponse>();
        var b = await second.Content.ReadFromJsonAsync<CreateGarmentResponse>();
        Assert.Equal(a!.Garment.Id, b!.Garment.Id);
        Assert.True(b.Deduplicated);
        Assert.Equal(2, Directory.GetFiles(factory.UploadsPath, "*.webp").Length);
    }

    [Fact]
    public async Task Uploading_same_pixels_with_different_metadata_deduplicates()
    {
        // Different EXIF artist and filename, identical pixels: only a hash over
        // the normalized bytes (EXIF stripped) can deduplicate these two files.
        var client = factory.CreateClient();
        using var firstContent = TestImages.Multipart(
            "original.jpg",
            TestImages.CreateJpeg(800, 600, artist: "Camera A"));
        using var secondContent = TestImages.Multipart(
            "copia.jpg",
            TestImages.CreateJpeg(800, 600, artist: "Camera B"));

        var first = await client.PostAsync("/api/garments", firstContent);
        var second = await client.PostAsync("/api/garments", secondContent);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var a = await first.Content.ReadFromJsonAsync<CreateGarmentResponse>();
        var b = await second.Content.ReadFromJsonAsync<CreateGarmentResponse>();
        Assert.Equal(a!.Garment.Id, b!.Garment.Id);
        Assert.True(b.Deduplicated);
        Assert.Equal(2, Directory.GetFiles(factory.UploadsPath, "*.webp").Length);
    }

    [Fact]
    public Task Upload_accepts_png_and_serves_it_as_webp() =>
        AssertUploadServesWebpAsync("camisa.png", TestImages.CreatePng(800, 600));

    [Fact]
    public Task Upload_accepts_webp_and_serves_it_as_webp() =>
        AssertUploadServesWebpAsync("camisa.webp", TestImages.CreateWebp(800, 600));

    [Fact]
    public async Task Corrupt_file_returns_400_and_persists_nothing()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/garments",
            TestImages.Multipart("x.jpg", new byte[] { 0xFF, 0xD8, 0xFF, 0x00, 0x01 }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var list = await factory.CreateClient().GetFromJsonAsync<GarmentListResponse>("/api/garments");
        Assert.Empty(list!.Items);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Upload_rejects_unsupported_formats_with_415()
    {
        var client = factory.CreateClient();
        (string Name, byte[] Bytes)[] cases =
        [
            ("x.gif", [0x47, 0x49, 0x46, 0x38, 0x39, 0x61]),
            ("x.jpg", [0x47, 0x49, 0x46, 0x38, 0x39, 0x61]),
            ("x.webp", [0xFF, 0xD8, 0x00])
        ];

        foreach (var (name, bytes) in cases)
        {
            using var content = TestImages.Multipart(name, bytes);
            var response = await client.PostAsync("/api/garments", content);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }

        var list = await client.GetFromJsonAsync<GarmentListResponse>("/api/garments");
        Assert.Empty(list!.Items);
    }

    [Fact]
    public async Task Upload_rejects_files_over_ten_megabytes_with_413()
    {
        var bytes = new byte[10 * 1024 * 1024 + 1];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;

        using var content = TestImages.Multipart("grande.jpg", bytes);
        var response = await factory.CreateClient().PostAsync("/api/garments", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Analyzer_failure_still_saves_garment_with_zero_confidence()
    {
        using var failing = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IGarmentAnalyzer, ThrowingAnalyzer>()));

        var response = await failing.CreateClient().PostAsync("/api/garments",
            TestImages.Multipart("ok.jpg", TestImages.CreateJpeg(600, 600)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateGarmentResponse>();
        Assert.Equal(0, body!.Garment.Confidence);
    }

    [Fact]
    public async Task Media_endpoint_serves_uploaded_images_with_immutable_cache()
    {
        var client = factory.CreateClient();
        var upload = await client.PostAsync("/api/garments",
            TestImages.Multipart("camisa.jpg", TestImages.CreateJpeg(800, 600)));
        var body = await upload.Content.ReadFromJsonAsync<CreateGarmentResponse>();

        foreach (var url in new[] { body!.Garment.ImageUrl, body.Garment.ThumbUrl })
        {
            var response = await client.GetAsync(url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(
                "public, max-age=31536000, immutable",
                response.Headers.CacheControl?.ToString());
        }
    }

    [Fact]
    public async Task Media_endpoint_returns_404_for_unknown_file()
    {
        var response = await factory.CreateClient()
            .GetAsync($"/media/{new string('a', 64)}.webp");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FileStorage_failed_publish_leaves_no_corrupt_file_or_temp_file()
    {
        var root = Path.Combine(factory.UploadsPath, "blocked");
        Directory.CreateDirectory(root);
        var normalized = await NormalizeAsync();

        // A directory at the final name makes the atomic rename fail, which is
        // the same failure shape as a disk-full or sharing violation.
        var target = Path.Combine(root, $"{normalized.ContentHash}.webp");
        Directory.CreateDirectory(target);

        var storage = CreateStorage(root);
        await Assert.ThrowsAsync<IOException>(() => storage.SaveAsync(normalized, default));

        Assert.True(Directory.Exists(target));
        Assert.Empty(Directory.GetFiles(root, "*.webp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
    }

    private async Task AssertUploadServesWebpAsync(string fileName, Stream image)
    {
        using (image)
        using (var content = TestImages.Multipart(fileName, image))
        {
            var client = factory.CreateClient();
            var response = await client.PostAsync("/api/garments", content);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<CreateGarmentResponse>();

            var media = await client.GetAsync(body!.Garment.ImageUrl);
            Assert.Equal(HttpStatusCode.OK, media.StatusCode);
            Assert.Equal("image/webp", media.Content.Headers.ContentType?.MediaType);

            var bytes = await media.Content.ReadAsByteArrayAsync();
            Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal("WEBP", Encoding.ASCII.GetString(bytes, 8, 4));
        }
    }

    private static async Task<NormalizedImage> NormalizeAsync()
    {
        using var jpeg = TestImages.CreateJpeg(320, 240);
        return await new MagickImageNormalizer().NormalizeAsync(jpeg, default);
    }

    private static FileStorage CreateStorage(string root) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Root"] = root })
            .Build());
}
