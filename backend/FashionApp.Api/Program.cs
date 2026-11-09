using System.Text.RegularExpressions;
using FashionApp.Api.Features.Digest;
using FashionApp.Api.Features.Garments;
using FashionApp.Api.Features.Outfits;
using FashionApp.Api.Features.Security;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Demo;
using FashionApp.Api.Infrastructure.Feeds;
using FashionApp.Api.Infrastructure.RateLimiting;
using FashionApp.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

// Manual wardrobe flow: images and attributes are user-provided; no external client
// is registered anywhere in the API.
EnvFile.Load(AppContext.BaseDirectory, Directory.GetCurrentDirectory());

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(builder.Configuration["Cors:AllowedOrigin"] ?? "http://localhost:5173")
     .AllowAnyHeader().AllowAnyMethod()
     // The UI shows the quota reset in minutes, so the browser must be able to
     // read Retry-After on a 429 cross-origin response.
     .WithExposedHeaders("Retry-After")));

builder.Services.AddRateLimiter(ClientRateLimiting.Configure);

// Resolved through DI (not read eagerly here) so WebApplicationFactory's
// in-memory overrides are already applied when the paths are first used.
builder.Services.AddSingleton(sp => DataPaths.Resolve(
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<IHostEnvironment>().ContentRootPath));

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlite(sp.GetRequiredService<DataPaths>().ConnectionString));

builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    return new AppSecurityOptions(
        configuration["App:Token"] ?? "",
        configuration.GetValue("Demo:Enabled", false));
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RuleBasedOutfitSuggester>();
builder.Services.AddSingleton<IOutfitSuggester>(sp =>
    sp.GetRequiredService<RuleBasedOutfitSuggester>());
builder.Services.AddScoped<IOutfitSuggestionService, OutfitSuggestionService>();
builder.Services.AddSingleton<IDigestSummarizer, RuleBasedDigestSummarizer>();

builder.Services.AddSingleton<IImageNormalizer, MagickImageNormalizer>();
builder.Services.AddSingleton<FileStorage>();

// The digest has its own provider switch because feeds are not AI: they can be
// real RSS even when the content is processed locally. Unknown values fail fast.
var digestProvider = builder.Configuration["Digest:Provider"]?.Trim() ?? "";
if (digestProvider.Length == 0)
{
    digestProvider = builder.Environment.IsDevelopment()
        ? "fake"
        : "rss";
}

if (digestProvider.Equals("rss", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IFeedFetcher, HttpFeedFetcher>();
}
else if (digestProvider.Equals("fake", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IFeedFetcher, FakeFeedFetcher>();
}
else
{
    throw new InvalidOperationException(
        $"Proveedor de feeds desconocido '{digestProvider}'. Usa 'rss' o 'fake'.");
}

builder.Services.AddScoped<IDigestGenerator, DigestGenerator>();
builder.Services.AddSingleton<DigestRefreshGuard>();

// The public demo is a zero-cost, read-only snapshot: it must never start the
// daily job (which would fetch feeds and call the AI). The check runs at
// resolution time so WebApplicationFactory's in-memory Demo:Enabled override is
// already part of the final configuration.
builder.Services.AddSingleton<IHostedService>(sp =>
{
    if (sp.GetRequiredService<IConfiguration>().GetValue("Demo:Enabled", false))
        return new NoopHostedService();

    return ActivatorUtilities.CreateInstance<DigestBackgroundService>(sp);
});

var app = builder.Build();

var dataPaths = app.Services.GetRequiredService<DataPaths>();
var demoEnabled = app.Configuration.GetValue("Demo:Enabled", false);

StartupChecks.ValidateSecurity(
    app.Environment.EnvironmentName,
    app.Configuration["App:Token"],
    demoEnabled,
    app.Configuration.GetValue(StartupChecks.AllowAnonymousKey, false));

app.UseCors();
app.UseRateLimiter();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    utc = DateTimeOffset.UtcNow.ToString("O")
}));

app.MapGet("/api/config", (IConfiguration configuration) => Results.Ok(new
{
    demo = configuration.GetValue("Demo:Enabled", false)
}));

app.MapGarmentEndpoints();
app.MapGarmentUploadEndpoint();
app.MapOutfitEndpoints();
app.MapOutfitSuggestEndpoint();
app.MapDigestEndpoints();
app.MapDigestRefreshEndpoint();

app.MapGet("/media/{fileName}", (string fileName, HttpContext ctx, DataPaths paths) =>
{
    if (!Regex.IsMatch(fileName, @"^[a-f0-9]{64}(-thumb)?\.webp$"))
        return Results.NotFound();

    var path = Path.Combine(paths.UploadsPath, fileName);
    if (!File.Exists(path))
        return Results.NotFound();

    ctx.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
    return Results.File(path, "image/webp", enableRangeProcessing: true);
});

// Single origin in production: the API serves the compiled SPA from wwwroot.
app.UseSpa();

using (var scope = app.Services.CreateScope())
{
    dataPaths.EnsureDirectories();

    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Repair placeholder links from older demo seeds even when the instance is
    // personal: example.com URLs never come from real feeds.
    await DemoSeeder.RepairLegacyDemoLinksAsync(db, CancellationToken.None);

    if (demoEnabled)
    {
        await DemoSeeder.EnsureOnlyDemoDataAsync(db);
        await DemoSeeder.SeedAsync(db, dataPaths.UploadsPath);
    }
}

app.Run();

public partial class Program;
