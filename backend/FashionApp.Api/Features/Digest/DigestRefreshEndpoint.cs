using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FashionApp.Api.Domain;
using FashionApp.Api.Features.Garments;
using FashionApp.Api.Features.Security;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.RateLimiting;
using Microsoft.EntityFrameworkCore;
using DigestEntity = FashionApp.Api.Domain.Digest;

namespace FashionApp.Api.Features.Digest;

/// <summary>
/// Admin-only regeneration of today's digest. Unlike the public <c>today</c>
/// read, this endpoint is a write: it is blocked in demo mode, requires
/// <c>X-Admin-Secret</c> (not the app token) and is rate limited per IP on top
/// of the one-hour cooldown enforced by <see cref="DigestRefreshGuard"/>.
/// </summary>
public static class DigestRefreshEndpoint
{
    public const string AdminSecretHeader = "X-Admin-Secret";

    private const string NotGeneratedDetail =
        "No se pudo generar el digest; se conserva el anterior.";

    public static IEndpointRouteBuilder MapDigestRefreshEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/digest/refresh", HandleAsync)
            .BlockWritesInDemo()
            .RequireRateLimiting(ClientRateLimiting.Policy);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IConfiguration configuration,
        IDigestGenerator generator,
        DigestRefreshGuard guard,
        AppDbContext db,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var provided = context.Request.Headers[AdminSecretHeader].ToString();
        if (string.IsNullOrEmpty(provided))
            return Results.Unauthorized();

        var expected = configuration["Digest:AdminSecret"] ?? "";
        if (expected.Length == 0)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Refresh no configurado",
                detail: "Definí Digest:AdminSecret para habilitar el refresh manual.");
        }

        if (!SecretEquals(expected, provided))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Acceso denegado",
                detail: "El secreto de administración no es válido.");
        }

        // The secret is valid but a run already happened in the last hour:
        // reject before touching the AI provider.
        if (!guard.TryBegin())
        {
            var remaining = guard.Remaining() ?? DigestRefreshGuard.Cooldown;
            context.Response.Headers.RetryAfter = Math.Ceiling(remaining.TotalSeconds)
                .ToString(CultureInfo.InvariantCulture);

            return Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Refresh en espera",
                detail: "Ya se generó el digest hace menos de una hora. " +
                        "Esperá antes de volver a intentarlo.");
        }

        DigestEntity? digest;
        try
        {
            digest = await generator.GenerateAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client went away before a result existed: do not punish the
            // next admin attempt with an hour of cooldown.
            guard.Reset();
            throw;
        }
        catch (Exception exception)
        {
            // A provider timeout or database failure must not keep the guard
            // occupied: release it and give the admin a clear 502.
            guard.Reset();
            loggerFactory
                .CreateLogger("FashionApp.Api.Features.Digest.DigestRefresh")
                .LogError(exception, "Falló la generación manual del digest.");

            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Digest no disponible",
                detail: NotGeneratedDetail);
        }

        if (digest is null)
        {
            // The generator leaves the previous digest untouched on an empty
            // run, so there is nothing to roll back here.
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Digest no disponible",
                detail: NotGeneratedDetail);
        }

        var rows = await db.DigestItems
            .AsNoTracking()
            .Where(item => item.DigestId == digest.Id)
            .OrderBy(item => item.SortOrder)
            .Select(item => new DigestItemRow(
                item.Id,
                item.Title,
                item.Url,
                item.Source,
                item.Summary,
                item.StyleTagsJson,
                item.ImageUrl,
                item.PublishedAt))
            .ToListAsync(cancellationToken);

        // Same privacy rule as GET /api/digest/today: the wardrobe is personal
        // data, so it is loaded once at most and only for its owner.
        IReadOnlyList<Garment> activeGarments = [];
        if (context.HasValidToken())
            activeGarments = await DigestEndpoints.LoadActiveGarmentsAsync(db, cancellationToken);

        var items = rows
            .Select(row =>
            {
                var tags = DigestEndpoints.ParseStyleTags(row.StyleTagsJson);
                IReadOnlyList<GarmentDto> related = activeGarments.Count > 0
                    ? DigestMatching.Match(activeGarments, tags)
                    : [];

                return new DigestItemDto(
                    row.Id,
                    row.Title,
                    row.Url,
                    row.Source,
                    row.Summary,
                    tags,
                    row.ImageUrl,
                    row.PublishedAt,
                    related);
            })
            .ToList();

        return Results.Ok(new DigestDto(digest.Date, digest.Summary, digest.GeneratedAt, items));
    }

    private static bool SecretEquals(string expected, string provided) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(provided));

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
