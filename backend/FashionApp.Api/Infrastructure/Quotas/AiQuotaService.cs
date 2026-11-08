using System.Globalization;
using FashionApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FashionApp.Api.Infrastructure.Quotas;

public interface IAiQuota
{
    Task<bool> TryConsumeAsync(string kind, CancellationToken ct);
}

public static class AiQuotaKinds
{
    public const string Analysis = "analysis";
    public const string Suggestion = "suggestion";
}

public static class QuotaResults
{
    /// <summary>
    /// Builds the <c>429</c> response with a <c>Retry-After</c> in seconds that
    /// tells the client when the UTC counter resets at midnight. The message
    /// names the exhausted budget (<c>análisis</c> or <c>sugerencias</c>).
    /// </summary>
    public static IResult TooManyRequests(HttpContext context, string kind)
    {
        var now = DateTimeOffset.UtcNow;
        var seconds = (int)Math.Ceiling((now.Date.AddDays(1) - now.UtcDateTime).TotalSeconds);

        context.Response.Headers["Retry-After"] =
            Math.Max(1, seconds).ToString(CultureInfo.InvariantCulture);

        var budget = kind == AiQuotaKinds.Suggestion ? "sugerencias" : "análisis";
        return Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Cuota diaria agotada",
            detail: $"Se alcanzó el límite de {budget} de IA para hoy (UTC).");
    }
}

/// <summary>
/// Enforces the per-day AI budget with a row per <c>(UTC date, kind)</c>.
/// Consumption is a single conditional upsert, which SQLite executes atomically:
/// concurrent requests can never both take the last unit and the denied path
/// does not depend on a read-then-write snapshot. The existing 5 s busy timeout
/// lets a writer wait for the lock instead of surfacing <c>SQLITE_BUSY</c>.
/// </summary>
public sealed class AiQuotaService(AppDbContext db, IConfiguration configuration) : IAiQuota
{
    private const int DefaultDailyAnalysisLimit = 50;
    private const int DefaultDailySuggestionLimit = 20;

    private const string ConsumeSql = """
        INSERT INTO "UsageCounters" ("Date", "Kind", "Count")
        VALUES ({0}, {1}, 1)
        ON CONFLICT ("Date", "Kind") DO UPDATE SET "Count" = "Count" + 1
        WHERE "Count" < {2};
        """;

    public async Task<bool> TryConsumeAsync(string kind, CancellationToken ct)
    {
        var limit = ResolveLimit(kind);
        if (limit <= 0)
            return false;

        var today = DateOnly.FromDateTime(DateTime.UtcNow)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Rows affected: 1 when the row was inserted or incremented, 0 when the
        // conditional update was skipped because the limit is already exhausted.
        var affected = await db.Database.ExecuteSqlRawAsync(
            ConsumeSql,
            new object[] { today, kind, limit },
            ct);

        return affected > 0;
    }

    private int ResolveLimit(string kind) => kind switch
    {
        AiQuotaKinds.Suggestion => configuration.GetValue(
            "Ai:DailySuggestionLimit", DefaultDailySuggestionLimit),
        _ => configuration.GetValue("Ai:DailyAnalysisLimit", DefaultDailyAnalysisLimit)
    };
}
