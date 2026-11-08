using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace FashionApp.Api.Features.Security;

/// <summary>
/// Static access configuration for the instance. <see cref="Token"/> is empty
/// for local/test runs, where no header is required.
/// </summary>
public sealed record AppSecurityOptions(string Token, bool DemoEnabled);

public static class AccessFilters
{
    public const string TokenHeader = "X-App-Token";

    /// <summary>
    /// Rejects data requests when the personal instance has a token configured
    /// and the request does not carry a matching <c>X-App-Token</c>. The token
    /// is optional so the local development and test hosts stay usable.
    /// </summary>
    public static TBuilder RequireToken<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(RequireTokenAsync);

    /// <summary>
    /// Makes the public demo read-only: every POST/PATCH/DELETE is rejected
    /// before the handler runs, so no image is stored and no AI is called.
    /// </summary>
    public static TBuilder BlockWritesInDemo<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(BlockWritesInDemoAsync);

    private static ValueTask<object?> RequireTokenAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<AppSecurityOptions>();
        if (string.IsNullOrEmpty(options.Token))
            return next(context);

        var provided = context.HttpContext.Request.Headers[TokenHeader].ToString();
        if (!TokenEquals(options.Token, provided))
            return ValueTask.FromResult<object?>(Results.Unauthorized());

        return next(context);
    }

    private static ValueTask<object?> BlockWritesInDemoAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<AppSecurityOptions>();
        var method = context.HttpContext.Request.Method;

        if (options.DemoEnabled
            && (HttpMethods.IsPost(method)
                || HttpMethods.IsPatch(method)
                || HttpMethods.IsDelete(method)))
        {
            return ValueTask.FromResult<object?>(Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Modo demo",
                detail: "La demo pública es de solo lectura: no se pueden modificar prendas."));
        }

        return next(context);
    }

    private static bool TokenEquals(string expected, string provided)
    {
        if (string.IsNullOrEmpty(provided))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(provided));
    }
}
