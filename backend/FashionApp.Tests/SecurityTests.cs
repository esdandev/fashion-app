using System.Net;
using System.Text;
using FashionApp.Api.Domain;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Demo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed class SecurityTests
{
    [Theory]
    [InlineData("Production", "", false, false, true)]
    [InlineData("Production", "   ", false, false, true)]
    [InlineData("Production", "secreto", false, false, false)]
    [InlineData("Production", "", false, true, false)]
    [InlineData("Production", "", true, false, false)]
    [InlineData("Development", "", false, false, false)]
    [InlineData("Testing", "", false, false, false)]
    public void Production_personal_instance_requires_a_token(
        string environment,
        string token,
        bool demo,
        bool allowAnonymous,
        bool shouldThrow)
    {
        void Validate() => StartupChecks.ValidateSecurity(
            environment, token, demo, allowAnonymous);

        if (shouldThrow)
        {
            var error = Assert.Throws<InvalidOperationException>(Validate);
            Assert.Contains("App:Token", error.Message);
        }
        else
        {
            Validate();
        }
    }

    [Fact]
    public async Task Demo_refuses_to_start_with_garments_outside_the_manifest()
    {
        using var factory = new TestAppFactory { Demo = true };
        Directory.CreateDirectory(factory.UploadsPath);

        // A personal garment in the demo database is exactly the data-leak the
        // startup guard must stop before the app serves a single request.
        await using (var db = TestDb.Create(factory.DbPath))
        {
            db.Garments.Add(TestData.NewGarment("Prenda personal", GarmentCategory.Top));
            await db.SaveChangesAsync();
        }

        var error = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(error);
        Assert.Contains("no puede usar esta base de datos", error.ToString());
    }

    [Fact]
    public async Task Personal_instance_requires_token()
    {
        using var factory = new TestAppFactory { Token = "secreto", Demo = false };
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/garments")).StatusCode);

        using (var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/garments"))
        {
            wrong.Headers.Add("X-App-Token", "incorrecto");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(wrong)).StatusCode);
        }

        client.DefaultRequestHeaders.Add("X-App-Token", "secreto");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/garments")).StatusCode);
    }

    [Fact]
    public async Task Demo_instance_blocks_writes_but_allows_reads()
    {
        using var factory = new TestAppFactory { Demo = true };
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/garments")).StatusCode);

        using var content = TestImages.Multipart("a.jpg", TestImages.CreateJpeg(200, 200));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/garments", content)).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await db.Garments.Select(g => g.Id).FirstAsync();

        using var patch = new StringContent(
            """{"name":"Cambio bloqueado"}""", Encoding.UTF8, "application/json");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PatchAsync($"/api/garments/{id}", patch)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.DeleteAsync($"/api/garments/{id}")).StatusCode);
    }

    [Fact]
    public async Task Demo_write_guard_wins_over_token_filter()
    {
        // The explicit ruling: demo writes must be 403 even when App:Token is set
        // and the request carries no token (the guard runs before the token filter).
        using var factory = new TestAppFactory { Token = "secreto", Demo = true };
        var client = factory.CreateClient();

        using var content = TestImages.Multipart("a.jpg", TestImages.CreateJpeg(200, 200));
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsync("/api/garments", content)).StatusCode);
    }

    [Fact]
    public async Task Demo_seed_is_idempotent_and_serves_assets()
    {
        using var factory = new TestAppFactory { Demo = true };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var first = await db.Garments.CountAsync();
        await DemoSeeder.SeedAsync(db, factory.UploadsPath);
        Assert.Equal(first, await db.Garments.CountAsync());
        Assert.InRange(first, 12, 15);

        var garment = await db.Garments.AsNoTracking().FirstAsync();
        var imagePath = Path.Combine(factory.UploadsPath, garment.ImagePath);
        var thumbPath = Path.Combine(factory.UploadsPath, garment.ThumbPath);
        Assert.True(File.Exists(imagePath), $"Falta el archivo sembrado '{imagePath}'.");
        Assert.True(File.Exists(thumbPath), $"Falta la miniatura sembrada '{thumbPath}'.");

        var client = factory.CreateClient();
        var media = await client.GetAsync($"/media/{garment.ImagePath}");
        Assert.Equal(HttpStatusCode.OK, media.StatusCode);
        Assert.Equal("image/webp", media.Content.Headers.ContentType?.MediaType);
    }
}
