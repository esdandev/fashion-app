using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionApp.Api.Infrastructure.RateLimiting;

/// <summary>
/// Effective per-IP rate limit configuration. It is enabled by default outside
/// Testing; the suite stays stable because Testing defaults to disabled, while
/// an explicit <c>RateLimiting:Enabled=true</c> can opt in for the dedicated
/// integration test.
/// </summary>
public sealed record ClientRateLimitOptions(bool Enabled, int PermitLimit);

public static class ClientRateLimiting
{
    public const string Policy = "per-ip";

    public const int DefaultPermitLimit = 60;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static ClientRateLimitOptions Resolve(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // An explicit value wins (the rate-limit test opts back in inside
        // Testing); otherwise Testing is disabled and every other environment
        // uses the documented default of enabled.
        var explicitlyEnabled = configuration.GetValue<bool?>("RateLimiting:Enabled");
        var enabled = explicitlyEnabled ?? !environment.IsEnvironment("Testing");

        var permitLimit = configuration.GetValue(
            "RateLimiting:PermitLimit",
            DefaultPermitLimit);

        return new ClientRateLimitOptions(
            enabled,
            permitLimit > 0 ? permitLimit : DefaultPermitLimit);
    }

    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.OnRejected = async (context, cancellationToken) =>
        {
            if (context.HttpContext.Response.HasStarted)
                return;

            // Retry-After is exposed in CORS so the client can explain the wait.
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Ceiling(Window.TotalSeconds)
                    .ToString(CultureInfo.InvariantCulture);

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Demasiadas solicitudes",
                Detail =
                    "Se alcanzó el límite de solicitudes por minuto. " +
                    "Esperá un momento y volvé a intentarlo."
            };

            await context.HttpContext.Response.WriteAsJsonAsync(
                problem,
                options: null,
                contentType: "application/problem+json",
                cancellationToken);
        };

        options.AddPolicy(Policy, httpContext =>
        {
            // Resolve per request so configuration overrides applied by the
            // integration host are honored (the partition itself is cached).
            var services = httpContext.RequestServices;
            var clientOptions = Resolve(
                services.GetRequiredService<IConfiguration>(),
                services.GetRequiredService<IHostEnvironment>());

            var partitionKey =
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida";

            if (!clientOptions.Enabled)
                return RateLimitPartition.GetNoLimiter(partitionKey);

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = clientOptions.PermitLimit,
                    Window = Window,
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        });
    }
}
