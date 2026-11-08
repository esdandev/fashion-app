using System.Text.RegularExpressions;
using FashionApp.Api.Features.Garments;
using FashionApp.Api.Features.Security;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Ai;
using FashionApp.Api.Infrastructure.Demo;
using FashionApp.Api.Infrastructure.Quotas;
using FashionApp.Api.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(builder.Configuration["Cors:AllowedOrigin"] ?? "http://localhost:5173")
     .AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default")));

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
else if (aiProvider.Length == 0
    || aiProvider.Equals("fake", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IGarmentAnalyzer, FakeGarmentAnalyzer>();
}
else
{
    throw new InvalidOperationException(
        $"Proveedor de IA desconocido '{aiOptions.Provider}'. Usa 'fake' o 'gemini'.");
}

var app = builder.Build();

app.UseCors();

app.MapGet("/", () => "Hello World!");

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    utc = DateTimeOffset.UtcNow.ToString("O")
}));

app.MapGarmentEndpoints();
app.MapGarmentUploadEndpoint();
app.MapGarmentReanalyzeEndpoint();

app.MapGet("/media/{fileName}", (string fileName, HttpContext ctx, IConfiguration config) =>
{
    if (!Regex.IsMatch(fileName, @"^[a-f0-9]{64}(-thumb)?\.webp$"))
        return Results.NotFound();

    var path = Path.Combine(config["Storage:Root"] ?? "data/uploads", fileName);
    if (!File.Exists(path))
        return Results.NotFound();

    ctx.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
    return Results.File(path, "image/webp", enableRangeProcessing: true);
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    EnsureSqliteDirectory(db.Database.GetConnectionString());
    db.Database.Migrate();

    if (app.Configuration.GetValue("Demo:Enabled", false))
    {
        await DemoSeeder.SeedAsync(
            db,
            app.Configuration["Storage:Root"] ?? "data/uploads");
    }
}

app.Run();

static void EnsureSqliteDirectory(string? connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
        return;

    var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
    if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:")
        return;

    var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
    if (!string.IsNullOrEmpty(directory))
        Directory.CreateDirectory(directory);
}

public partial class Program;
