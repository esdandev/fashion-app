using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FashionApp.Api.Features.Garments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public sealed class AnalyzeConfirmTests : IDisposable
{
    private readonly TestAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Analyze_creates_no_garment_row_but_stores_reusable_files()
    {
        var client = factory.CreateClient();
        var analysis = await AnalyzeAsync(client, TestImages.CreateJpeg(800, 600));

        Assert.Matches("^[a-f0-9]{64}$", analysis.ContentHash);
        Assert.Null(analysis.ExistingGarmentId);
        Assert.Equal("Camisa de lino", analysis.Analysis.Name);
        Assert.Equal(0.9, analysis.Analysis.Confidence);
        Assert.Equal($"/media/{analysis.ContentHash}.webp", analysis.ImageUrl);
        Assert.Equal($"/media/{analysis.ContentHash}-thumb.webp", analysis.ThumbUrl);

        var list = await client.GetFromJsonAsync<GarmentListResponse>("/api/garments");
        Assert.Empty(list!.Items);

        Assert.Equal(2, Directory.GetFiles(factory.UploadsPath, "*.webp").Length);
    }

    [Fact]
    public async Task Confirm_after_analyze_creates_garment_with_confirmed_values()
    {
        var client = factory.CreateClient();
        var analysis = await AnalyzeAsync(client, TestImages.CreateJpeg(800, 600));

        var response = await client.PostAsJsonAsync(
            "/api/garments/confirm",
            ConfirmRequest(analysis.ContentHash, name: "Camisa de lino azul", colorName: "celeste", colorHex: "#123456"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateGarmentResponse>();
        Assert.False(body!.Deduplicated);
        Assert.Equal("Camisa de lino azul", body.Garment.Name);
        Assert.Equal("celeste", body.Garment.ColorName);
        Assert.Equal("#123456", body.Garment.ColorHex);
        Assert.Equal(0.9, body.Garment.Confidence);

        var list = await client.GetFromJsonAsync<GarmentListResponse>("/api/garments");
        Assert.Single(list!.Items);
        Assert.Equal(body.Garment.Id, list.Items[0].Id);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FashionApp.Api.Infrastructure.AppDbContext>();
        Assert.Equal(1, await db.Garments.CountAsync());
    }

    [Fact]
    public async Task Confirm_with_existing_hash_dedupes_without_creating_a_row()
    {
        var client = factory.CreateClient();
        var analysis = await AnalyzeAsync(client, TestImages.CreateJpeg(800, 600));

        var first = await client.PostAsJsonAsync(
            "/api/garments/confirm",
            ConfirmRequest(analysis.ContentHash, name: "Primera confirmación"));
        var firstBody = await first.Content.ReadFromJsonAsync<CreateGarmentResponse>();

        var second = await client.PostAsJsonAsync(
            "/api/garments/confirm",
            ConfirmRequest(analysis.ContentHash, name: "Segunda confirmación"));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<CreateGarmentResponse>();
        Assert.True(secondBody!.Deduplicated);
        Assert.Equal(firstBody!.Garment.Id, secondBody.Garment.Id);
        Assert.Equal("Primera confirmación", secondBody.Garment.Name);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FashionApp.Api.Infrastructure.AppDbContext>();
        Assert.Equal(1, await db.Garments.CountAsync());
    }

    [Fact]
    public async Task Analyze_of_confirmed_photo_returns_existing_id_and_analysis()
    {
        var client = factory.CreateClient();
        var first = await AnalyzeAsync(client, TestImages.CreateJpeg(800, 600));
        var confirm = await client.PostAsJsonAsync(
            "/api/garments/confirm",
            ConfirmRequest(first.ContentHash, name: "Camisa confirmada", colorName: "azul", colorHex: "#0000ff"));
        var created = await confirm.Content.ReadFromJsonAsync<CreateGarmentResponse>();

        var again = await AnalyzeAsync(client, TestImages.CreateJpeg(800, 600));

        Assert.Equal(first.ContentHash, again.ContentHash);
        Assert.Equal(created!.Garment.Id, again.ExistingGarmentId);
        Assert.Equal("Camisa confirmada", again.Analysis.Name);
        Assert.NotNull(again.CurrentGarment);
        Assert.Equal("Camisa confirmada", again.CurrentGarment!.Name);
        Assert.Equal("azul", again.CurrentGarment.ColorName);
        Assert.Equal("#0000ff", again.CurrentGarment.ColorHex);
    }

    [Fact]
    public async Task Reupload_after_edit_returns_current_values_and_keeps_the_hex()
    {
        var client = factory.CreateClient();
        var first = await AnalyzeAsync(client, TestImages.CreateJpeg(800, 600));
        var confirm = await client.PostAsJsonAsync(
            "/api/garments/confirm",
            ConfirmRequest(first.ContentHash, name: "Nombre original", colorName: "verde", colorHex: "#00aa55"));
        var created = await confirm.Content.ReadFromJsonAsync<CreateGarmentResponse>();

        // Re-upload opens the form from the current columns, not the original
        // confirm snapshot.
        var reupload = await AnalyzeAsync(client, TestImages.CreateJpeg(800, 600));
        Assert.Equal("Nombre original", reupload.CurrentGarment!.Name);
        Assert.Equal("#00aa55", reupload.CurrentGarment.ColorHex);

        // Saving an edited name while the form still has the hex, plus a legacy
        // client that sends an empty hex, must never erase the swatch.
        var patch = await client.PatchAsJsonAsync(
            $"/api/garments/{created!.Garment.Id}",
            new { name = "Nombre editado", colorHex = "" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var patched = await patch.Content.ReadFromJsonAsync<GarmentDto>();
        Assert.Equal("Nombre editado", patched!.Name);
        Assert.Equal("#00aa55", patched.ColorHex);

        var third = await AnalyzeAsync(client, TestImages.CreateJpeg(800, 600));
        Assert.Equal("Nombre editado", third.CurrentGarment!.Name);
        Assert.Equal("#00aa55", third.CurrentGarment.ColorHex);
    }

    [Fact]
    public async Task Demo_blocks_analyze_and_confirm()
    {
        using var demo = new TestAppFactory { Demo = true };
        var client = demo.CreateClient();

        using var content = TestImages.Multipart("a.jpg", TestImages.CreateJpeg(200, 200));
        var analyze = await client.PostAsync("/api/garments/analyze", content);
        var confirm = await client.PostAsJsonAsync(
            "/api/garments/confirm",
            ConfirmRequest(new string('a', 64), name: "Bloqueada"));

        Assert.Equal(HttpStatusCode.Forbidden, analyze.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, confirm.StatusCode);
    }

    [Fact]
    public async Task Quota_is_consumed_by_analyze_only()
    {
        using var limited = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                    { ["Ai:DailyAnalysisLimit"] = "1" })));
        var client = limited.CreateClient();

        var first = await AnalyzeAsync(client, TestImages.CreateJpeg(600, 600));

        using var secondContent = TestImages.Multipart(
            "segunda.jpg",
            TestImages.CreateJpeg(601, 601));
        var second = await client.PostAsync("/api/garments/analyze", secondContent);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);

        // Confirm does not touch the AI quota, so it still succeeds after the
        // analyze budget is exhausted.
        var confirm = await client.PostAsJsonAsync(
            "/api/garments/confirm",
            ConfirmRequest(first.ContentHash, name: "Confirmada sin cuota"));

        Assert.Equal(HttpStatusCode.Created, confirm.StatusCode);
    }

    [Fact]
    public async Task Analyze_and_confirm_require_the_token()
    {
        using var personal = new TestAppFactory { Token = "secreto" };
        var client = personal.CreateClient();

        using (var content = TestImages.Multipart("a.jpg", TestImages.CreateJpeg(200, 200)))
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await client.PostAsync("/api/garments/analyze", content)).StatusCode);
        }

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(
                "/api/garments/confirm",
                ConfirmRequest(new string('a', 64), name: "Sin token"))).StatusCode);

        client.DefaultRequestHeaders.Add("X-App-Token", "secreto");
        using var authorized = TestImages.Multipart("a.jpg", TestImages.CreateJpeg(200, 200));
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsync("/api/garments/analyze", authorized)).StatusCode);
    }

    [Fact]
    public async Task Config_is_public_and_reports_demo_mode()
    {
        using var personal = new TestAppFactory();
        var personalConfig = await personal.CreateClient()
            .GetFromJsonAsync<JsonElement>("/api/config");
        Assert.False(personalConfig.GetProperty("demo").GetBoolean());

        using var demo = new TestAppFactory { Demo = true };
        var demoConfig = await demo.CreateClient()
            .GetFromJsonAsync<JsonElement>("/api/config");
        Assert.True(demoConfig.GetProperty("demo").GetBoolean());
    }

    [Fact]
    public async Task Confirm_rejects_unknown_content_hash_or_missing_files()
    {
        var client = factory.CreateClient();

        var invalid = await client.PostAsJsonAsync(
            "/api/garments/confirm",
            ConfirmRequest("not-a-hash", name: "Inválida"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var missing = await client.PostAsJsonAsync(
            "/api/garments/confirm",
            ConfirmRequest(new string('b', 64), name: "Sin archivos"));
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
    }

    [Fact]
    public async Task Confirm_rejects_input_outside_the_closed_vocabulary_and_bounds()
    {
        var client = factory.CreateClient();
        var hash = new string('c', 64);

        ConfirmGarmentRequest[] cases =
        [
            ConfirmRequest(hash, "Camisa") with { ColorHex = "azul" },
            ConfirmRequest(hash, "Camisa") with { Seasons = ["monsoon"] },
            ConfirmRequest(hash, "Camisa") with { StyleTags = ["inventado"] },
            ConfirmRequest(hash, "Camisa") with
            {
                StyleTags = ["minimalista", "casual", "formal", "vintage", "y2k"]
            },
            ConfirmRequest(hash, new string('a', 121)),
            ConfirmRequest(hash, "Camisa") with { Notes = new string('a', 401) }
        ];

        foreach (var request in cases)
        {
            var response = await client.PostAsJsonAsync("/api/garments/confirm", request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Reuploading_an_archived_photo_restores_the_same_garment()
    {
        var client = factory.CreateClient();
        var image = TestImages.CreateJpeg(803, 603);

        var first = await AnalyzeAsync(client, image);
        var confirm = await client.PostAsJsonAsync(
            "/api/garments/confirm",
            ConfirmRequest(first.ContentHash, name: "Prenda archivada"));
        var created = await confirm.Content.ReadFromJsonAsync<CreateGarmentResponse>();

        var archive = await client.DeleteAsync($"/api/garments/{created!.Garment.Id}");
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);

        // The archived row is ignored during analyze; the user gets a fresh
        // proposal instead of a reference to a hidden garment.
        var again = await AnalyzeAsync(client, TestImages.CreateJpeg(803, 603));
        Assert.Equal(first.ContentHash, again.ContentHash);
        Assert.Null(again.ExistingGarmentId);

        var restore = await client.PostAsJsonAsync(
            "/api/garments/confirm",
            ConfirmRequest(again.ContentHash, name: "Prenda restaurada"));
        var restored = await restore.Content.ReadFromJsonAsync<CreateGarmentResponse>();

        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Equal(created.Garment.Id, restored!.Garment.Id);
        Assert.Equal("Prenda restaurada", restored.Garment.Name);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider
            .GetRequiredService<FashionApp.Api.Infrastructure.AppDbContext>();
        var saved = await db.Garments.AsNoTracking().SingleAsync();
        Assert.False(saved.IsArchived);
        Assert.Equal("Prenda restaurada", saved.Name);
    }

    [Fact]
    public async Task Analyze_and_upload_map_no_garment_to_422()
    {
        using var noGarment = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<FashionApp.Api.Infrastructure.Ai.IGarmentAnalyzer, NoGarmentAnalyzer>()));
        var client = noGarment.CreateClient();

        using var analyzeContent = TestImages.Multipart("a.jpg", TestImages.CreateJpeg(200, 200));
        var analyze = await client.PostAsync("/api/garments/analyze", analyzeContent);

        using var uploadContent = TestImages.Multipart("b.jpg", TestImages.CreateJpeg(201, 201));
        var upload = await client.PostAsync("/api/garments", uploadContent);

        foreach (var response in new[] { analyze, upload })
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(
                "no parece contener una prenda",
                body.GetProperty("detail").GetString() ?? "");
        }
    }

    private static async Task<AnalyzeGarmentResponse> AnalyzeAsync(
        HttpClient client,
        Stream image)
    {
        using (image)
        using (var content = TestImages.Multipart("camisa.jpg", image))
        {
            var response = await client.PostAsync("/api/garments/analyze", content);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<AnalyzeGarmentResponse>();
            Assert.NotNull(body);
            return body!;
        }
    }

    private static ConfirmGarmentRequest ConfirmRequest(
        string contentHash,
        string name,
        string colorName = "beige",
        string colorHex = "#f5f5dc") =>
        new(
            contentHash,
            name,
            "top",
            "camisa",
            colorName,
            colorHex,
            "liso",
            ["spring", "summer"],
            3,
            "lino",
            ["minimalista", "casual"],
            null,
            0.9);
}
