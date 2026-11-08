using System.Text.Json;
using FashionApp.Api.Domain;
using FashionApp.Api.Features.Security;
using FashionApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FashionApp.Api.Features.Garments;

public static class GarmentEndpoints
{
    public static IEndpointRouteBuilder MapGarmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/garments");

        group.MapGet("/", async (
            string? category,
            string? color,
            string? season,
            string? cursor,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var query = GarmentQuery.Apply(db.Garments, category, color, season, cursor);
            return Results.Ok(await GarmentQuery.ToPageAsync(query, cancellationToken));
        }).RequireToken();

        group.MapPatch("/{id:guid}", async (
            Guid id,
            PatchGarmentRequest request,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var garment = await db.Garments
                .FirstOrDefaultAsync(g => g.Id == id && !g.IsArchived, cancellationToken);

            if (garment is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Prenda no encontrada",
                    detail: "No existe una prenda activa con ese identificador.");
            }

            var category = GarmentCategory.Top;
            if (request.Category is not null)
            {
                if (!Enum.TryParse(request.Category, ignoreCase: true, out category)
                    || !Enum.IsDefined(category))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Solicitud inválida",
                        detail: "La categoría no es válida.");
                }
            }

            if (request.Formality is < 1 or > 5)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Solicitud inválida",
                    detail: "La formalidad debe estar entre 1 y 5.");
            }

            if (request.Name is not null)
            {
                var name = request.Name.Trim();
                if (name.Length == 0)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Solicitud inválida",
                        detail: "El nombre no puede estar vacío.");
                }

                garment.Name = name;
            }

            if (request.Category is not null)
                garment.Category = category;
            if (request.ColorName is not null)
                garment.ColorName = request.ColorName.Trim();
            if (request.ColorHex is not null)
                garment.ColorHex = request.ColorHex.Trim();
            if (request.Formality is int formality)
                garment.Formality = formality;
            if (request.Seasons is not null)
                garment.Seasons = JoinSeasons(request.Seasons);
            if (request.StyleTags is not null)
                garment.StyleTagsJson = SerializeStyleTags(request.StyleTags);
            if (request.Notes is not null)
                garment.Notes = request.Notes.Trim();

            garment.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(GarmentQuery.ToDto(garment));
        }).BlockWritesInDemo().RequireToken();

        group.MapDelete("/{id:guid}", async (
            Guid id,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var garment = await db.Garments
                .FirstOrDefaultAsync(g => g.Id == id && !g.IsArchived, cancellationToken);

            if (garment is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Prenda no encontrada",
                    detail: "No existe una prenda activa con ese identificador.");
            }

            garment.IsArchived = true;
            garment.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }).BlockWritesInDemo().RequireToken();

        return app;
    }

    private static string JoinSeasons(IEnumerable<string> seasons) =>
        string.Join(
            ',',
            seasons
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim()));

    private static string SerializeStyleTags(IEnumerable<string> styleTags) =>
        JsonSerializer.Serialize(
            styleTags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .ToArray());
}
