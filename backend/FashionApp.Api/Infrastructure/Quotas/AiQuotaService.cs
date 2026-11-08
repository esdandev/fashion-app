using FashionApp.Api.Domain;
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
}

public static class QuotaResults
{
    /// <summary>
    /// Builds the <c>429</c> response with a <c>Retry-After</c> in seconds that
    /// tells the client when the UTC counter resets at midnight.
    /// </summary>
    public static IResult TooManyRequests(HttpContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var seconds = (int)Math.Ceiling((now.Date.AddDays(1) - now.UtcDateTime).TotalSeconds);

        context.Response.Headers["Retry-After"] =
            Math.Max(1, seconds).ToString(System.Globalization.CultureInfo.InvariantCulture);

        return Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Cuota diaria agotada",
            detail: "Se alcanzó el límite de análisis de IA para hoy (UTC).");
    }
}

/// <summary>
/// Enforces the per-day AI budget with a row per <c>(UTC date, kind)</c>.
/// A denied request never increments the counter, so the limit is exact even
/// when many requests arrive after it is exhausted.
/// </summary>
public sealed class AiQuotaService(AppDbContext db, IConfiguration configuration) : IAiQuota
{
    private const int DefaultDailyAnalysisLimit = 50;

    public async Task<bool> TryConsumeAsync(string kind, CancellationToken ct)
    {
        var limit = configuration.GetValue("Ai:DailyAnalysisLimit", DefaultDailyAnalysisLimit);
        if (limit <= 0)
            return false;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var counter = await db.UsageCounters
            .FirstOrDefaultAsync(c => c.Date == today && c.Kind == kind, ct);

        if (counter is null)
        {
            counter = new UsageCounter { Date = today, Kind = kind, Count = 1 };
            db.UsageCounters.Add(counter);
        }
        else
        {
            if (counter.Count >= limit)
            {
                await transaction.RollbackAsync(ct);
                return false;
            }

            counter.Count++;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
