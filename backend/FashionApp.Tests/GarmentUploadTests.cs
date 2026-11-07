using System.Net;
using System.Net.Http.Json;
using FashionApp.Api.Features.Garments;
using FashionApp.Api.Infrastructure.Ai;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

public sealed class GarmentUploadTests : IDisposable
{
    private readonly TestAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Upload_creates_garment_and_writes_two_files()
    {
        var client = factory.CreateClient();
        using var content = TestImages.Multipart("camisa.jpg", TestImages.CreateJpeg(800, 600));
        var response = await client.PostAsync("/api/garments", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateGarmentResponse>();
        Assert.False(body!.Deduplicated);
        Assert.Equal(2, Directory.GetFiles(factory.UploadsPath, "*.webp").Length);
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
}
