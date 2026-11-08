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

            var validationError = GarmentInputRules.Validate(
                request.Name,
                requireName: false,
                request.Category,
                subcategory: null,
                request.ColorName,
                request.ColorHex,
                pattern: null,
                material: null,
                request.Notes,
                request.Formality,
                request.Seasons,
                request.StyleTags);

            if (validationError is not null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Solicitud inválida",
                    detail: validationError);
            }

            var category = GarmentAnalysisMapping.ParseCategory(request.Category);

            if (request.Name is not null)
                garment.Name = request.Name.Trim();
            if (request.Category is not null)
                garment.Category = category;
            if (request.ColorName is not null)
                garment.ColorName = request.ColorName.Trim();
            // An empty hex from an old form must not erase a swatch the user
            // already saved; only a non-blank value updates the column.
            if (!string.IsNullOrWhiteSpace(request.ColorHex))
                garment.ColorHex = request.ColorHex.Trim();
            if (request.Formality is int formality)
                garment.Formality = formality;
            if (request.Seasons is not null)
                garment.Seasons = string.Join(',', GarmentInputRules.NormalizeSeasons(request.Seasons));
            if (request.StyleTags is not null)
                garment.StyleTagsJson = JsonSerializer.Serialize(
                    GarmentInputRules.NormalizeStyleTags(request.StyleTags));
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
}
