using FashionApp.Api.Domain;
using FashionApp.Api.Features.Garments;
using FashionApp.Api.Features.Security;
using FashionApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FashionApp.Api.Features.Outfits;

public static class OutfitEndpoints
{
    public static IEndpointRouteBuilder MapOutfitEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/outfits");

        group.MapGet("/", async (AppDbContext db, CancellationToken cancellationToken) =>
        {
            // Single projection with the items and their garments; AsNoTracking keeps
            // this a read-only query and the projection avoids N+1 lazy access.
            // Archived garments are intentionally included: a saved outfit keeps its
            // photos even after the garment is archived from the wardrobe.
            var rows = await db.Outfits
                .AsNoTracking()
                .OrderByDescending(o => o.IsFavorite)
                .ThenByDescending(o => o.CreatedAt)
                .Select(o => new OutfitRow(
                    o.Id,
                    o.Name,
                    o.Occasion,
                    o.Source,
                    o.Rationale,
                    o.IsFavorite,
                    o.CreatedAt,
                    o.Items
                        .OrderBy(i => i.SortOrder)
                        .Select(i => new OutfitItemRow(
                            new GarmentQuery.GarmentRow(
                                i.Garment.Id,
                                i.Garment.Name,
                                i.Garment.Category,
                                i.Garment.ColorName,
                                i.Garment.ColorHex,
                                i.Garment.Formality,
                                i.Garment.Confidence,
                                i.Garment.Seasons,
                                i.Garment.StyleTagsJson,
                                i.Garment.ContentHash,
                                i.Garment.CreatedAt),
                            i.Layer,
                            i.SortOrder))
                        .ToList()))
                .ToListAsync(cancellationToken);

            return Results.Ok(rows.Select(ToDto).ToList());
        }).RequireToken();

        return app;
    }

    private static OutfitDto ToDto(OutfitRow row) => new(
        row.Id,
        row.Name,
        row.Occasion,
        row.Source == OutfitSource.Ai ? "ai" : "manual",
        row.Rationale,
        row.IsFavorite,
        row.CreatedAt,
        row.Items
            .Select(item => new OutfitItemDto(
                GarmentQuery.ToDto(item.Garment),
                item.Layer,
                item.SortOrder))
            .ToList());

    private sealed record OutfitRow(
        Guid Id,
        string Name,
        string? Occasion,
        OutfitSource Source,
        string? Rationale,
        bool IsFavorite,
        DateTimeOffset CreatedAt,
        List<OutfitItemRow> Items);

    private sealed record OutfitItemRow(GarmentQuery.GarmentRow Garment, int Layer, int SortOrder);
}
