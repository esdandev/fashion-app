using System.Globalization;
using System.Text;
using System.Text.Json;
using FashionApp.Api.Domain;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace FashionApp.Api.Features.Garments;

public static class GarmentQuery
{
    public const int PageSize = 24;

    private const string SqliteTimestampFormat = "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz";

    /// <summary>
    /// Applies the wardrobe filters and the opaque cursor. Results are ordered by
    /// <c>CreatedAt DESC, Id DESC</c>, which is the order the cursor was built for.
    /// </summary>
    public static IQueryable<Garment> Apply(
        IQueryable<Garment> source,
        string? category,
        string? color,
        string? season,
        string? cursor)
    {
        var query = source.AsNoTracking().Where(g => !g.IsArchived);

        if (!string.IsNullOrWhiteSpace(category))
        {
            if (Enum.TryParse<GarmentCategory>(category.Trim(), ignoreCase: true, out var parsedCategory)
                && Enum.IsDefined(parsedCategory))
            {
                query = query.Where(g => g.Category == parsedCategory);
            }
            else
            {
                // An unknown category cannot match anything; keep the response shape stable.
                return query.Where(_ => false);
            }
        }

        if (!string.IsNullOrWhiteSpace(color))
        {
            var normalizedColor = color.Trim().ToLowerInvariant();
            query = query.Where(g => g.ColorName.ToLower() == normalizedColor);
        }

        if (!string.IsNullOrWhiteSpace(season))
        {
            var seasonPattern = $"%{season.Trim()}%";
            query = query.Where(g => EF.Functions.Like(g.Seasons, seasonPattern));
        }

        if (!string.IsNullOrWhiteSpace(cursor)
            && TryDecodeCursor(cursor, out var cursorCreatedAt, out var cursorId))
        {
            // SQLite cannot compare DateTimeOffset columns either, so the keyset
            // comparison is done on the provider's sortable ISO text form.
            var cursorCreatedAtText = cursorCreatedAt.ToString(
                SqliteTimestampFormat,
                CultureInfo.InvariantCulture);
            var cursorIdText = cursorId.ToString("D").ToLowerInvariant();
            query = query.Where(g =>
                string.Compare(g.CreatedAt.ToString(), cursorCreatedAtText) < 0
                || (g.CreatedAt.ToString() == cursorCreatedAtText
                    && string.Compare(g.Id.ToString().ToLower(), cursorIdText) < 0));
        }

        // SQLite refuses to ORDER BY a DateTimeOffset column. The provider stores it as
        // sortable ISO text and every timestamp in this app is UTC, so order by its text
        // form instead.
        return query
            .OrderByDescending(g => g.CreatedAt.ToString())
            .ThenByDescending(g => g.Id);
    }

    public static async Task<GarmentListResponse> ToPageAsync(
        IQueryable<Garment> query,
        CancellationToken cancellationToken)
    {
        // Fetch one extra row to know whether a next cursor is needed. This is a
        // projection to DTO columns only; no tracked entities are materialized.
        var rows = await query
            .Take(PageSize + 1)
            .Select(g => new GarmentRow(
                g.Id,
                g.Name,
                g.Category,
                g.ColorName,
                g.ColorHex,
                g.Formality,
                g.Confidence,
                g.Seasons,
                g.StyleTagsJson,
                g.ContentHash,
                g.CreatedAt))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > PageSize;
        if (hasMore)
            rows.RemoveAt(PageSize);

        var items = rows.Select(ToDto).ToArray();
        var nextCursor = hasMore ? EncodeCursor(rows[^1].CreatedAt, rows[^1].Id) : null;

        return new GarmentListResponse(items, nextCursor);
    }

    public static GarmentDto ToDto(Garment garment) => new(
        garment.Id,
        garment.Name,
        CategoryToApi(garment.Category),
        garment.ColorName,
        garment.ColorHex,
        garment.Formality,
        garment.Confidence,
        ParseSeasons(garment.Seasons),
        ParseStyleTags(garment.StyleTagsJson),
        MediaUrl(garment.ContentHash),
        ThumbUrl(garment.ContentHash));

    private static GarmentDto ToDto(GarmentRow row) => new(
        row.Id,
        row.Name,
        CategoryToApi(row.Category),
        row.ColorName,
        row.ColorHex,
        row.Formality,
        row.Confidence,
        ParseSeasons(row.Seasons),
        ParseStyleTags(row.StyleTagsJson),
        MediaUrl(row.ContentHash),
        ThumbUrl(row.ContentHash));

    private static string CategoryToApi(GarmentCategory category) =>
        category.ToString().ToLowerInvariant();

    private static string[] ParseSeasons(string? seasons) =>
        string.IsNullOrWhiteSpace(seasons)
            ? []
            : seasons.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string[] ParseStyleTags(string? styleTagsJson)
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

    private static string MediaUrl(string contentHash) => $"/media/{contentHash}.webp";

    private static string ThumbUrl(string contentHash) => $"/media/{contentHash}-thumb.webp";

    private static string EncodeCursor(DateTimeOffset createdAt, Guid id) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
            $"{createdAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}:{id:D}"));

    private static bool TryDecodeCursor(
        string cursor,
        out DateTimeOffset createdAt,
        out Guid id)
    {
        createdAt = default;
        id = default;

        try
        {
            var payload = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor));
            var separator = payload.LastIndexOf(':');
            if (separator <= 0)
                return false;

            var ticks = long.Parse(payload[..separator], CultureInfo.InvariantCulture);
            id = Guid.Parse(payload[(separator + 1)..]);
            createdAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    private sealed record GarmentRow(
        Guid Id,
        string Name,
        GarmentCategory Category,
        string ColorName,
        string ColorHex,
        int Formality,
        double Confidence,
        string Seasons,
        string StyleTagsJson,
        string ContentHash,
        DateTimeOffset CreatedAt);
}
