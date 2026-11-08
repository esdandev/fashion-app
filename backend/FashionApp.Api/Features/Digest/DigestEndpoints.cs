using System.Text.Json;
using FashionApp.Api.Domain;
using FashionApp.Api.Features.Garments;
using FashionApp.Api.Features.Security;
using FashionApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;

namespace FashionApp.Api.Features.Digest;

public static class DigestEndpoints
{
    private const int CacheSeconds = 300;

    public static IEndpointRouteBuilder MapDigestEndpoints(this IEndpointRouteBuilder app)
    {
        // Public by design: the digest is not personal data and is the entry
        // point of the "Hoy" page. It is intentionally not rate limited (it is
        // a cached read) and it is never cacheable as public because the
        // relatedGarments section depends on the X-App-Token header.
        app.MapGet("/api/digest/today", async (
            AppDbContext db,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var row = await db.Digests
                .AsNoTracking()
                .OrderByDescending(d => d.Date)
                .Select(d => new DigestRow(
                    d.Id,
                    d.Date,
                    d.Summary,
                    d.GeneratedAt,
                    d.Items
                        .OrderBy(i => i.SortOrder)
                        .Select(i => new DigestItemRow(
                            i.Id,
                            i.Title,
                            i.Url,
                            i.Source,
                            i.Summary,
                            i.StyleTagsJson,
                            i.ImageUrl,
                            i.PublishedAt))
                        .ToList()))
                .FirstOrDefaultAsync(cancellationToken);

            if (row is null)
                return Results.NotFound();

            var etag = $"\"{row.Id:N}-{row.GeneratedAt.UtcTicks}\"";
            context.Response.Headers.ETag = etag;
            context.Response.Headers.CacheControl = $"private, max-age={CacheSeconds}";
            // The body changes with the token: a shared cache must key on it.
            context.Response.Headers.Vary = AccessFilters.TokenHeader;

            if (IfNoneMatchMatches(context.Request.Headers.IfNoneMatch, etag))
                return Results.StatusCode(StatusCodes.Status304NotModified);

            // The wardrobe is personal data: it is read once for the whole
            // response (never per item) and only when the request may see it.
            IReadOnlyList<Garment> activeGarments = [];
            if (context.HasValidToken())
                activeGarments = await LoadActiveGarmentsAsync(db, cancellationToken);

            var items = row.Items
                .Select(item =>
                {
                    var itemTags = ParseStyleTags(item.StyleTagsJson);
                    IReadOnlyList<GarmentDto> related = [];
                    if (activeGarments.Count > 0)
                        related = DigestMatching.Match(activeGarments, itemTags);

                    return new DigestItemDto(
                        item.Id,
                        item.Title,
                        item.Url,
                        item.Source,
                        item.Summary,
                        itemTags,
                        item.ImageUrl,
                        item.PublishedAt,
                        related);
                })
                .ToList();

            return Results.Ok(new DigestDto(row.Date, row.Summary, row.GeneratedAt, items));
        });

        return app;
    }

    /// <summary>
    /// Loads the columns the DTO and the matching need, without tracking and
    /// without materializing heavy fields such as notes or the analysis JSON.
    /// </summary>
    internal static async Task<IReadOnlyList<Garment>> LoadActiveGarmentsAsync(
        AppDbContext db,
        CancellationToken cancellationToken) =>
        await db.Garments
            .AsNoTracking()
            .Where(g => !g.IsArchived)
            .Select(g => new Garment
            {
                Id = g.Id,
                Name = g.Name,
                Category = g.Category,
                ColorName = g.ColorName,
                ColorHex = g.ColorHex,
                Formality = g.Formality,
                Confidence = g.Confidence,
                Seasons = g.Seasons,
                StyleTagsJson = g.StyleTagsJson,
                ContentHash = g.ContentHash,
                UpdatedAt = g.UpdatedAt
            })
            .ToListAsync(cancellationToken);

    internal static string[] ParseStyleTags(string? styleTagsJson)
    {
        if (string.IsNullOrWhiteSpace(styleTagsJson))
            return [];

        try
        {
            return JsonSerializer.Deserialize<string[]>(styleTagsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IfNoneMatchMatches(StringValues ifNoneMatch, string etag)
    {
        foreach (var value in ifNoneMatch)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            foreach (var tag in value.Split(
                         ',',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (tag == "*" || string.Equals(tag, etag, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    private sealed record DigestRow(
        Guid Id,
        DateOnly Date,
        string Summary,
        DateTimeOffset GeneratedAt,
        List<DigestItemRow> Items);

    private sealed record DigestItemRow(
        Guid Id,
        string Title,
        string Url,
        string Source,
        string Summary,
        string StyleTagsJson,
        string? ImageUrl,
        DateTimeOffset? PublishedAt);
}
