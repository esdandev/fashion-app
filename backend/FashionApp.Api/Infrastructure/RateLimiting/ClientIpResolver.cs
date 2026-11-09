using System.Net;
using Microsoft.Extensions.Primitives;

namespace FashionApp.Api.Infrastructure.RateLimiting;

/// <summary>
/// Resolves the visitor IP used as the rate-limit partition key.
///
/// In production the app runs behind Fly.io's edge proxy, which terminates TLS:
/// <see cref="Microsoft.AspNetCore.Http.ConnectionInfo.RemoteIpAddress"/> is the
/// proxy address, not the visitor. Fly injects <c>Fly-Client-IP</c>; when the app
/// is behind another trusted proxy, the leftmost <c>X-Forwarded-For</c> entry is
/// the next best signal. Both headers are trivially spoofable on a direct
/// connection, so the Machine must only be reachable through Fly's proxy (no
/// public IP/service bypassing the edge). See <c>docs/deploy.md</c>.
/// </summary>
public static class ClientIpResolver
{
    public const string FlyClientIpHeader = "Fly-Client-IP";
    public const string ForwardedForHeader = "X-Forwarded-For";
    public const string UnknownKey = "desconocida";

    public static string Resolve(HttpContext context)
    {
        var request = context.Request;

        var flyIp = FirstHeaderEntry(request.Headers[FlyClientIpHeader]);
        if (IsIpAddress(flyIp))
            return flyIp!;

        var forwardedIp = FirstHeaderEntry(request.Headers[ForwardedForHeader]);
        if (IsIpAddress(forwardedIp))
            return forwardedIp!;

        return context.Connection.RemoteIpAddress?.ToString() ?? UnknownKey;
    }

    /// <summary>
    /// Takes the first non-empty comma-separated entry of a header. For
    /// <c>X-Forwarded-For</c> that is exactly the leftmost value the client
    /// supplied; the rest are proxy hops and must not become the partition.
    /// </summary>
    private static string? FirstHeaderEntry(StringValues values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var comma = value.IndexOf(',');
            var entry = (comma >= 0 ? value[..comma] : value).Trim();
            return entry.Length == 0 ? null : entry;
        }

        return null;
    }

    private static bool IsIpAddress(string? value) =>
        value is not null && IPAddress.TryParse(value, out _);
}
