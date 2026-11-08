using System.Globalization;
using FashionApp.Api.Features.Digest;
using Microsoft.EntityFrameworkCore;

namespace FashionApp.Api.Infrastructure;

/// <summary>
/// Generates the digest once a day at <c>Digest:RunAt</c> (server local time)
/// and optionally right after startup when today's digest is still missing.
/// Every failure is logged and swallowed: a feed or AI outage must never take
/// the host down, and the previous digest stays available.
/// </summary>
public sealed class DigestBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<DigestBackgroundService> logger) : BackgroundService
{
    public const string DefaultRunAt = "07:00";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Never delay host startup with the first generation.
        await Task.Yield();

        var runAt = ParseRunAt();

        if (configuration.GetValue("Digest:GenerateOnStartup", true))
            await RunSafelyAsync(onlyWhenMissingToday: true, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DelayUntilNextRun(runAt, clock.GetLocalNow());
            logger.LogInformation(
                "Próxima generación del digest a las {RunAt} (en {Delay}).",
                runAt,
                delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunSafelyAsync(onlyWhenMissingToday: false, stoppingToken);
        }
    }

    private async Task RunSafelyAsync(bool onlyWhenMissingToday, CancellationToken ct)
    {
        try
        {
            await RunAsync(onlyWhenMissingToday, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host shutting down: not a failure.
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Falló la generación del digest; se conserva el anterior.");
        }
    }

    private async Task RunAsync(bool onlyWhenMissingToday, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        if (onlyWhenMissingToday)
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            if (await db.Digests.AsNoTracking().AnyAsync(row => row.Date == today, ct))
            {
                logger.LogInformation(
                    "Ya existe el digest de hoy ({Date}); no se regenera al arrancar.",
                    today);
                return;
            }
        }

        var generator = scope.ServiceProvider.GetRequiredService<IDigestGenerator>();
        var digest = await generator.GenerateAsync(ct);

        if (digest is null)
        {
            logger.LogWarning(
                "La generación no produjo un digest; se conserva el anterior.");
            return;
        }

        logger.LogInformation(
            "Digest generado para {Date} con {Count} ítems.",
            digest.Date,
            digest.Items.Count);
    }

    private TimeOnly ParseRunAt()
    {
        var raw = configuration["Digest:RunAt"];
        if (TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        logger.LogWarning(
            "Digest:RunAt='{RunAt}' no es una hora válida; se usa {Default}.",
            raw,
            DefaultRunAt);

        return TimeOnly.Parse(DefaultRunAt, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Server local wall-clock delay until the next <paramref name="runAt"/>.
    /// </summary>
    private static TimeSpan DelayUntilNextRun(TimeOnly runAt, DateTimeOffset localNow)
    {
        var next = localNow.Date.Add(runAt.ToTimeSpan());
        var delay = next - localNow.DateTime;

        if (delay <= TimeSpan.Zero)
            delay += TimeSpan.FromDays(1);

        return delay;
    }
}
