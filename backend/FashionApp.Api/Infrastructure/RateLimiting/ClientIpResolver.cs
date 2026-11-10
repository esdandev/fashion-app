using System.Net;
using Microsoft.Extensions.Primitives;

namespace FashionApp.Api.Infrastructure.RateLimiting;

/// <summary>
/// Resolves the visitor IP used as the rate-limit partition key.
///
/// Behind a trusted reverse proxy that terminates TLS,
/// <see cref="Microsoft.AspNetCore.Http.ConnectionInfo.RemoteIpAddress"/> is the
/// proxy address, not the visitor. The leftmost <c>X-Forwarded-For</c> entry is
/// the next best signal. The header is trivially spoofable on a direct
/// connection, so the app should only be reachable through that proxy (no
/// public IP bypassing it).
/// </summary>
public static class ClientIpResolver
{
    public const string ForwardedForHeader = "X-Forwarded-For";
    public const string UnknownKey = "desconocida";

    public static string Resolve(HttpContext context)
    {
        var request = context.Request;

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
