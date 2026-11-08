using FashionApp.Api.Domain;
using FashionApp.Api.Features.Garments;
using FashionApp.Api.Features.Security;
using FashionApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FashionApp.Api.Features.Outfits;

public static class OutfitEndpoints
{
    private const int MaxNameLength = 120;
    private const int MaxOccasionLength = 60;
    private const int MaxRationaleLength = 600;

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

        group.MapPost("/", async (
            CreateOutfitRequest request,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var nameError = ValidateName(request.Name);
            if (nameError is not null)
                return BadRequest(nameError);

            var occasionError = ValidateOccasion(request.Occasion);
            if (occasionError is not null)
                return BadRequest(occasionError);

            var (source, sourceError) = ParseSource(request.Source);
            if (sourceError is not null)
                return BadRequest(sourceError);

            var rationaleError = ValidateRationale(request.Rationale);
            if (rationaleError is not null)
                return BadRequest(rationaleError);

            var garmentIds = request.GarmentIds ?? [];
            if (HasDuplicates(garmentIds))
                return BadRequest(OutfitRules.DuplicateGarment);

            var (garments, error) = await LoadActiveGarmentsAsync(db, garmentIds, cancellationToken);
            if (error is not null)
                return error;

            var validation = OutfitRules.Validate(garments);
            if (!validation.IsValid)
                return BadRequest(validation.Error!);

            var now = DateTimeOffset.UtcNow;
            var outfit = new Outfit
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Occasion = NormalizeOptionalText(request.Occasion),
                Source = source,
                Rationale = NormalizeOptionalText(request.Rationale),
                IsFavorite = false,
                CreatedAt = now,
                UpdatedAt = now
            };
            outfit.Items.AddRange(BuildItems(outfit.Id, garments));

            db.Outfits.Add(outfit);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created($"/api/outfits/{outfit.Id}", ToDto(outfit));
        }).BlockWritesInDemo().RequireToken();

        group.MapPatch("/{id:guid}", async (
            Guid id,
            PatchOutfitRequest request,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var outfit = await db.Outfits
                .Include(o => o.Items)
                .ThenInclude(i => i.Garment)
                .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

            if (outfit is null)
                return NotFound();

            if (request.Name is not null)
            {
                var nameError = ValidateName(request.Name);
                if (nameError is not null)
                    return BadRequest(nameError);
            }

            var occasionError = ValidateOccasion(request.Occasion);
            if (occasionError is not null)
                return BadRequest(occasionError);

            List<Garment>? replacement = null;
            if (request.GarmentIds is not null)
            {
                if (HasDuplicates(request.GarmentIds))
                    return BadRequest(OutfitRules.DuplicateGarment);

                var (garments, error) = await LoadActiveGarmentsAsync(
                    db, request.GarmentIds, cancellationToken);
                if (error is not null)
                    return error;

                var validation = OutfitRules.Validate(garments);
                if (!validation.IsValid)
                    return BadRequest(validation.Error!);

                replacement = garments;
            }

            // Replacing the item set must be atomic with the metadata update: the
            // outfit can never be observed with half of the old and half of the new
            // pieces.
            IDbContextTransaction? transaction = null;
            try
            {
                if (replacement is not null)
                    transaction = await db.Database.BeginTransactionAsync(cancellationToken);

                if (request.Name is not null)
                    outfit.Name = request.Name.Trim();
                if (request.Occasion is not null)
                    outfit.Occasion = NormalizeOptionalText(request.Occasion);
                if (request.IsFavorite is bool isFavorite)
                    outfit.IsFavorite = isFavorite;
                if (replacement is not null)
                    ReplaceItems(db, outfit, replacement);

                outfit.UpdatedAt = DateTimeOffset.UtcNow;

                await db.SaveChangesAsync(cancellationToken);

                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
            }
            finally
            {
                if (transaction is not null)
                    await transaction.DisposeAsync();
            }

            return Results.Ok(ToDto(outfit));
        }).BlockWritesInDemo().RequireToken();

        group.MapDelete("/{id:guid}", async (
            Guid id,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var outfit = await db.Outfits
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

            if (outfit is null)
                return NotFound();

            // The join rows are removed by the configured cascade; the garments
            // themselves are never touched.
            db.Outfits.Remove(outfit);
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }).BlockWritesInDemo().RequireToken();

        return app;
    }

    private static async Task<(List<Garment> Garments, IResult? Error)> LoadActiveGarmentsAsync(
        AppDbContext db,
        IReadOnlyCollection<Guid> garmentIds,
        CancellationToken cancellationToken)
    {
        var distinctIds = garmentIds.Distinct().ToArray();
        if (distinctIds.Length == 0)
            return ([], null);

        var garments = await db.Garments
            .Where(g => distinctIds.Contains(g.Id) && !g.IsArchived)
            .ToListAsync(cancellationToken);

        // An unknown or archived id is a conflict, not a composition error: the
        // client must refresh its wardrobe before retrying.
        if (garments.Count != distinctIds.Length)
            return ([], Conflict());

        var byId = garments.ToDictionary(g => g.Id);
        return (distinctIds.Select(id => byId[id]).ToList(), null);
    }

    private static List<OutfitItem> BuildItems(Guid outfitId, IReadOnlyList<Garment> garments)
    {
        var items = new List<OutfitItem>(garments.Count);
        for (var index = 0; index < garments.Count; index++)
        {
            var garment = garments[index];
            items.Add(new OutfitItem
            {
                OutfitId = outfitId,
                GarmentId = garment.Id,
                Garment = garment,
                Layer = LayerFor(garment.Category),
                SortOrder = index
            });
        }

        return items;
    }

    private static void ReplaceItems(
        AppDbContext db,
        Outfit outfit,
        IReadOnlyList<Garment> garments)
    {
        var targetIds = garments.Select(g => g.Id).ToHashSet();

        foreach (var removed in outfit.Items
                     .Where(item => !targetIds.Contains(item.GarmentId))
                     .ToList())
        {
            db.OutfitItems.Remove(removed);
            outfit.Items.Remove(removed);
        }

        var existingByGarmentId = outfit.Items.ToDictionary(item => item.GarmentId);

        for (var index = 0; index < garments.Count; index++)
        {
            var garment = garments[index];
            var layer = LayerFor(garment.Category);

            if (existingByGarmentId.TryGetValue(garment.Id, out var existing))
            {
                // Reuse the tracked row so a kept garment is not deleted and
                // re-inserted (which would collide on the composite primary key).
                existing.Layer = layer;
                existing.SortOrder = index;
            }
            else
            {
                outfit.Items.Add(new OutfitItem
                {
                    OutfitId = outfit.Id,
                    GarmentId = garment.Id,
                    Garment = garment,
                    Layer = layer,
                    SortOrder = index
                });
            }
        }
    }

    /// <summary>Base pieces (including shoes) stack first, outerwear then accessories.</summary>
    private static int LayerFor(GarmentCategory category) => category switch
    {
        GarmentCategory.Outerwear => 1,
        GarmentCategory.Bag or GarmentCategory.Accessory => 2,
        _ => 0
    };

    private static bool HasDuplicates(IReadOnlyCollection<Guid> garmentIds) =>
        garmentIds.Distinct().Count() != garmentIds.Count;

    private static string? ValidateName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
            return "El nombre no puede estar vacío.";

        return trimmed.Length > MaxNameLength
            ? $"El nombre no puede superar los {MaxNameLength} caracteres."
            : null;
    }

    private static string? ValidateOccasion(string? occasion) =>
        occasion is not null && occasion.Trim().Length > MaxOccasionLength
            ? $"La ocasión no puede superar los {MaxOccasionLength} caracteres."
            : null;

    private static string? ValidateRationale(string? rationale) =>
        rationale is not null && rationale.Trim().Length > MaxRationaleLength
            ? $"La razón no puede superar los {MaxRationaleLength} caracteres."
            : null;

    /// <summary>
    /// Only the two values stored by the model are accepted. An omitted source
    /// keeps the manual default, so older clients keep working unchanged.
    /// </summary>
    private static (OutfitSource Source, string? Error) ParseSource(string? source)
    {
        var value = source?.Trim();
        if (string.IsNullOrEmpty(value))
            return (OutfitSource.Manual, null);

        return value.ToLowerInvariant() switch
        {
            "ai" => (OutfitSource.Ai, null),
            "manual" => (OutfitSource.Manual, null),
            _ => (OutfitSource.Manual, "La fuente debe ser 'ai' o 'manual'.")
        };
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IResult BadRequest(string detail) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Solicitud inválida",
        detail: detail);

    private static IResult NotFound() => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Outfit no encontrado",
        detail: "No existe un outfit con ese identificador.");

    private static IResult Conflict() => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Prendas no disponibles",
        detail: "Una o más prendas no existen o están archivadas.");

    private static OutfitDto ToDto(Outfit outfit) => new(
        outfit.Id,
        outfit.Name,
        outfit.Occasion,
        outfit.Source == OutfitSource.Ai ? "ai" : "manual",
        outfit.Rationale,
        outfit.IsFavorite,
        outfit.CreatedAt,
        outfit.Items
            .OrderBy(item => item.SortOrder)
            .Select(item => new OutfitItemDto(
                GarmentQuery.ToDto(item.Garment),
                item.Layer,
                item.SortOrder))
            .ToList());

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
