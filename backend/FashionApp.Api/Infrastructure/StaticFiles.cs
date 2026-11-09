using Microsoft.AspNetCore.StaticFiles;

namespace FashionApp.Api.Infrastructure;

/// <summary>
/// Serves the compiled SPA from <c>wwwroot</c> so production runs on a single
/// origin. Vite fingerprints every asset, so those can be cached forever;
/// <c>index.html</c> must never be, otherwise a new deploy keeps pointing at
/// old assets until the CDN cache is purged.
/// </summary>
public static class SpaStaticFiles
{
    public const string IndexFileName = "index.html";
    public const string IndexCacheControl = "no-cache";
    public const string ImmutableCacheControl = "public, max-age=31536000, immutable";

    /// <summary>
    /// Wires default files, static files and the SPA fallback. Call it after the
    /// API and media endpoints, right before <c>app.Run()</c>.
    /// </summary>
    public static void UseSpa(this WebApplication app)
    {
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ApplyCacheHeaders
        });

        // A catch-all fallback would answer an unknown /api or /media route with
        // index.html. Those clients expect JSON or an image, so keep their 404s.
        app.MapFallback("/api/{**path}", () => Results.NotFound());
        app.MapFallback("/media/{**path}", () => Results.NotFound());

        // Unknown SPA routes (e.g. /guardarropa) render the shell; the client
        // router decides what to show.
        app.MapFallbackToFile(IndexFileName, new StaticFileOptions
        {
            OnPrepareResponse = ApplyCacheHeaders
        });
    }

    private static void ApplyCacheHeaders(StaticFileResponseContext context)
    {
        context.Context.Response.Headers.CacheControl =
            IsIndex(context.File.Name)
                ? IndexCacheControl
                : ImmutableCacheControl;
    }

    private static bool IsIndex(string fileName) =>
        fileName.Equals(IndexFileName, StringComparison.OrdinalIgnoreCase);
}
