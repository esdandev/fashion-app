using System.Text.RegularExpressions;
using FashionApp.Api.Features.Digest;
using FashionApp.Api.Features.Garments;
using FashionApp.Api.Features.Outfits;
using FashionApp.Api.Features.Security;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Ai;
using FashionApp.Api.Infrastructure.Demo;
using FashionApp.Api.Infrastructure.Feeds;
using FashionApp.Api.Infrastructure.Quotas;
using FashionApp.Api.Infrastructure.RateLimiting;
using FashionApp.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

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
builder.Services.AddScoped<IAiQuota, AiQuotaService>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RuleBasedOutfitSuggester>();
builder.Services.AddScoped<IOutfitSuggestionService, OutfitSuggestionService>();

builder.Services.AddSingleton<IImageNormalizer, MagickImageNormalizer>();
builder.Services.AddSingleton<FileStorage>();

var aiSection = builder.Configuration.GetSection("Ai");
builder.Services.Configure<AiOptions>(aiSection);
var aiOptions = aiSection.Get<AiOptions>() ?? new AiOptions();
var aiProvider = aiOptions.Provider?.Trim() ?? "";
var usingFakeProvider = aiProvider.Length == 0
    || aiProvider.Equals("fake", StringComparison.OrdinalIgnoreCase);
var usingRealProvider = aiProvider.Equals("gemini", StringComparison.OrdinalIgnoreCase)
    || aiProvider.Equals("opencode", StringComparison.OrdinalIgnoreCase);

if (usingRealProvider)
{
    // Fail at startup, not on the first upload: a missing key would otherwise
    // surface as a silent Confidence = 0 for every garment.
    if (string.IsNullOrWhiteSpace(aiOptions.ApiKey))
    {
        throw new InvalidOperationException(
            $"Ai:Provider está configurado como '{aiProvider}' pero falta Ai:ApiKey. " +
            "Define la variable de entorno AI__APIKEY o cambia Ai:Provider a 'fake'.");
    }

    if (AiApiFamilyResolver.Resolve(aiOptions.Provider, aiOptions.Model)
        == AiApiFamily.OpenAi)
    {
        // OpenCode serves DeepSeek/GLM/Qwen through OpenAI Chat Completions,
        // not through the Google-compatible endpoint.
        builder.Services.AddHttpClient<IGarmentAnalyzer, OpenAiGarmentAnalyzer>();
        builder.Services.AddHttpClient<IOutfitSuggester, OpenAiOutfitSuggester>();
        builder.Services.AddHttpClient<IDigestSummarizer, OpenAiDigestSummarizer>();
    }
    else
    {
        builder.Services.AddHttpClient<IGarmentAnalyzer, GeminiGarmentAnalyzer>();
        builder.Services.AddHttpClient<IOutfitSuggester, GeminiOutfitSuggester>();
        builder.Services.AddHttpClient<IDigestSummarizer, GeminiDigestSummarizer>();
    }
}
else if (usingFakeProvider)
{
    builder.Services.AddSingleton<IGarmentAnalyzer, FakeGarmentAnalyzer>();
    builder.Services.AddSingleton<IOutfitSuggester, FakeOutfitSuggester>();
    builder.Services.AddSingleton<IDigestSummarizer, FakeDigestSummarizer>();
}
else
{
    throw new InvalidOperationException(
        $"Proveedor de IA desconocido '{aiOptions.Provider}'. Usa 'fake', 'gemini' u 'opencode'.");
}

// The digest has its own provider switch because feeds are not AI: they can be
// real RSS even when the summarizer is the fake. Unknown values fail fast.
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

app.Logger.LogInformation(
    "IA configurada: provider={Provider}, model={Model}, keyConfigured={KeyConfigured}",
    aiOptions.Provider,
    AiRequestTargetFactory.ResolveModel(aiOptions.Provider, aiOptions.Model),
    !string.IsNullOrWhiteSpace(aiOptions.ApiKey));

StartupChecks.ValidateSecurity(
    app.Environment.EnvironmentName,
    app.Configuration["App:Token"],
    demoEnabled,
    app.Configuration.GetValue(StartupChecks.AllowAnonymousKey, false));

if (app.Environment.IsProduction() && !demoEnabled && usingFakeProvider)
{
    // A fake provider in Production saves placeholder attributes for real
    // photos. It is legitimate for the demo, but never for the personal instance.
    app.Logger.LogWarning(
        "Ai:Provider=fake en Production: el análisis de prendas usará datos de ejemplo. " +
        "Configurá AI__PROVIDER=gemini y AI__APIKEY para análisis real.");
}

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

app.MapGet("/api/ai/status", (IConfiguration configuration) => Results.Ok(new
{
    provider = configuration["Ai:Provider"] ?? "fake",
    model = AiRequestTargetFactory.ResolveModel(
        configuration["Ai:Provider"],
        configuration["Ai:Model"]),
    apiFamily = AiApiFamilyResolver.Resolve(
        configuration["Ai:Provider"],
        configuration["Ai:Model"]).ToString(),
    baseUrl = configuration["Ai:BaseUrl"] ?? "",
    keyConfigured = !string.IsNullOrWhiteSpace(configuration["Ai:ApiKey"]),
    digestProvider = configuration["Digest:Provider"] ?? "rss"
})).RequireToken();

app.MapGarmentEndpoints();
app.MapGarmentUploadEndpoint();
app.MapGarmentAnalyzeEndpoint();
app.MapGarmentConfirmEndpoint();
app.MapGarmentReanalyzeEndpoint();
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

    if (demoEnabled)
    {
        await DemoSeeder.EnsureOnlyDemoDataAsync(db);
        await DemoSeeder.SeedAsync(db, dataPaths.UploadsPath);
    }
}

app.Run();

public partial class Program;
