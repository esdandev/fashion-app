using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;

namespace FashionApp.Api.Infrastructure;

/// <summary>
/// Serves the compiled SPA from <c>wwwroot</c> so production runs on a single
/// origin. Vite fingerprints every asset under <c>/assets/</c>, so those can be
/// cached forever; other public files (favicon, demo images) are not
/// fingerprinted and get a short cache. <c>index.html</c> must never be cached,
/// otherwise a new deploy keeps pointing at old assets until the CDN cache is
/// purged.
/// </summary>
public static class SpaStaticFiles
{
    public const string IndexFileName = "index.html";
    public const string IndexCacheControl = "no-cache";
    public const string ImmutableCacheControl = "public, max-age=31536000, immutable";
    public const string ShortCacheControl = "public, max-age=3600";

    /// <summary>
    /// A Vite fingerprint such as <c>TodayPage-DiwrgTda.js</c> or
    /// <c>index-B0a1C2d3.css</c>. Eight or more URL-safe characters after a
    /// separator is the shape Vite emits.
    /// </summary>
    private static readonly Regex HashLikeFileName = new(
        @"[-.][A-Za-z0-9_-]{8,}\.[A-Za-z0-9]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
        context.Context.Response.Headers.CacheControl = GetCacheControl(context);
    }

    internal static string GetCacheControl(StaticFileResponseContext context)
    {
        if (IsIndex(context.File.Name))
            return IndexCacheControl;

        return IsFingerprinted(context.Context.Request.Path, context.File.Name)
            ? ImmutableCacheControl
            : ShortCacheControl;
    }

    private static bool IsIndex(string fileName) =>
        fileName.Equals(IndexFileName, StringComparison.OrdinalIgnoreCase);

    private static bool IsFingerprinted(string? requestPath, string fileName)
    {
        // Vite writes every generated asset under /assets/; the path check keeps
        // the rule valid even if a future build changes the filename shape.
        if (requestPath is not null
            && requestPath.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return HashLikeFileName.IsMatch(fileName);
    }
}
