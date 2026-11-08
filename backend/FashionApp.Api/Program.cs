using System.Text.RegularExpressions;
using FashionApp.Api.Features.Garments;
using FashionApp.Api.Features.Outfits;
using FashionApp.Api.Features.Security;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Ai;
using FashionApp.Api.Infrastructure.Demo;
using FashionApp.Api.Infrastructure.Quotas;
using FashionApp.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(builder.Configuration["Cors:AllowedOrigin"] ?? "http://localhost:5173")
     .AllowAnyHeader().AllowAnyMethod()));

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

builder.Services.AddSingleton<IImageNormalizer, MagickImageNormalizer>();
builder.Services.AddSingleton<FileStorage>();

var aiSection = builder.Configuration.GetSection("Ai");
builder.Services.Configure<AiOptions>(aiSection);
var aiOptions = aiSection.Get<AiOptions>() ?? new AiOptions();
var aiProvider = aiOptions.Provider?.Trim() ?? "";
var usingFakeProvider = aiProvider.Length == 0
    || aiProvider.Equals("fake", StringComparison.OrdinalIgnoreCase);

if (aiProvider.Equals("gemini", StringComparison.OrdinalIgnoreCase))
{
    // Fail at startup, not on the first upload: a missing key would otherwise
    // surface as a silent Confidence = 0 for every garment.
    if (string.IsNullOrWhiteSpace(aiOptions.ApiKey))
    {
        throw new InvalidOperationException(
            "Ai:Provider está configurado como 'gemini' pero falta Ai:ApiKey. " +
            "Define la variable de entorno AI__APIKEY o cambia Ai:Provider a 'fake'.");
    }

    builder.Services.AddHttpClient<IGarmentAnalyzer, GeminiGarmentAnalyzer>();
}
else if (usingFakeProvider)
{
    builder.Services.AddSingleton<IGarmentAnalyzer, FakeGarmentAnalyzer>();
}
else
{
    throw new InvalidOperationException(
        $"Proveedor de IA desconocido '{aiOptions.Provider}'. Usa 'fake' o 'gemini'.");
}

var app = builder.Build();

var dataPaths = app.Services.GetRequiredService<DataPaths>();
var demoEnabled = app.Configuration.GetValue("Demo:Enabled", false);

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

app.MapGet("/", () => "Hello World!");

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
app.MapGarmentAnalyzeEndpoint();
app.MapGarmentConfirmEndpoint();
app.MapGarmentReanalyzeEndpoint();
app.MapOutfitEndpoints();

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
