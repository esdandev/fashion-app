using FashionApp.Api.Domain;

namespace FashionApp.Api.Features.Outfits;

/// <summary>
/// Trust boundary for outfit proposals. Any draft coming from a model (or any
/// other untrusted source) must pass through here before persistence: unknown or
/// archived ids are discarded and the shared composition rules are applied.
/// </summary>
public static class OutfitSuggestionValidator
{
    /// <summary>
    /// Resolves each draft against the active wardrobe, drops drafts with unknown
    /// or archived ids, applies <see cref="OutfitRules"/> and removes duplicates
    /// that reference the same set of garments. The original order is preserved.
    /// </summary>
    public static IReadOnlyList<OutfitDraft> KeepValid(
        IReadOnlyList<OutfitDraft> drafts,
        IReadOnlyList<Garment> wardrobe)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentNullException.ThrowIfNull(wardrobe);

        var activeById = new Dictionary<Guid, Garment>();
        foreach (var garment in wardrobe)
        {
            if (garment.IsArchived)
                continue;

            activeById.TryAdd(garment.Id, garment);
        }

        var kept = new List<OutfitDraft>(drafts.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var draft in drafts)
        {
            var ids = draft.GarmentIds ?? [];
            if (ids.Length == 0)
                continue;

            var resolved = new List<Garment>(ids.Length);
            var allKnown = true;
            foreach (var id in ids)
            {
                if (!activeById.TryGetValue(id, out var garment))
                {
                    allKnown = false;
                    break;
                }

                resolved.Add(garment);
            }

            if (!allKnown)
                continue;

            // A draft that repeats a category or a garment is invalid under the
            // same rules the manual CRUD uses, so it is dropped instead of fixed.
            if (!OutfitRules.Validate(resolved).IsValid)
                continue;

            // A model may phrase the same combination twice. Deduplicate by the
            // set of ids, ignoring their order.
            var key = BuildKey(ids);
            if (!seen.Add(key))
                continue;

            kept.Add(draft);
        }

        return kept;
    }

    private static string BuildKey(IEnumerable<Guid> ids) =>
        string.Join(
            ",",
            ids.OrderBy(id => id).Select(id => id.ToString("N")));
}
