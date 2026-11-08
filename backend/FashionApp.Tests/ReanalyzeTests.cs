using System.Net;
using System.Net.Http.Json;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static TestData;

public sealed class ReanalyzeTests : IDisposable
{
    private readonly TestAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Reanalyze_returns_proposal_without_changing_garment()
    {
        var id = await SeedGarmentWithImage(factory, "Camisa vieja");
        var response = await factory.CreateClient().PostAsync($"/api/garments/{id}/reanalyze", null);
        var proposal = await response.Content.ReadFromJsonAsync<GarmentAnalysis>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Camisa de lino", proposal!.Name);       // lo que devuelve el fake
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("Camisa vieja", (await db.Garments.FindAsync(id))!.Name);
    }

    [Fact]
    public async Task Reanalyze_returns_404_for_unknown_garment()
    {
        var response = await factory.CreateClient()
            .PostAsync($"/api/garments/{Guid.NewGuid()}/reanalyze", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Reanalyze_returns_409_when_image_file_is_missing()
    {
        // SeedOneGarment points at hashed file names that were never written.
        var id = await SeedOneGarment(factory);
        var response = await factory.CreateClient().PostAsync($"/api/garments/{id}/reanalyze", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Reanalyze_returns_422_when_the_model_says_there_is_no_garment()
    {
        var id = await SeedGarmentWithImage(factory, "Camisa vieja");
        using var noGarment = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IGarmentAnalyzer, NoGarmentAnalyzer>()));

        var response = await noGarment.CreateClient()
            .PostAsync($"/api/garments/{id}/reanalyze", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Reanalyze_returns_empty_proposal_when_analyzer_fails()
    {
        var id = await SeedGarmentWithImage(factory, "Camisa vieja");
        using var failing = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IGarmentAnalyzer, ThrowingAnalyzer>()));

        var response = await failing.CreateClient()
            .PostAsync($"/api/garments/{id}/reanalyze", null);
        var proposal = await response.Content.ReadFromJsonAsync<GarmentAnalysis>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, proposal!.Confidence);
        Assert.Equal("Prenda", proposal.Name);
    }
}
