using System.Text;
using System.Text.Json;
using FashionApp.Api.Domain;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Ai;
using FashionApp.Api.Infrastructure.Feeds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using DigestEntity = FashionApp.Api.Domain.Digest;

namespace FashionApp.Api.Features.Digest;

public interface IDigestGenerator
{
    /// <summary>
    /// Builds today's digest from the configured feeds. Returns <c>null</c> when
    /// there is nothing worth persisting; in that case the previous digest is
    /// left untouched.
    /// </summary>
    Task<DigestEntity?> GenerateAsync(CancellationToken ct);
}

/// <summary>
/// Orchestrates one digest run: read the feeds, drop stale and repeated notes,
/// ask the summarizer for an untrusted proposal, validate it and upsert the row
/// for the current date in a single transaction.
/// </summary>
public sealed class DigestGenerator(
    IFeedFetcher feedFetcher,
    IDigestSummarizer summarizer,
    AppDbContext db,
    IConfiguration configuration,
    IHostEnvironment environment,
    TimeProvider clock,
    ILogger<DigestGenerator> logger) : IDigestGenerator
{
    public const int DefaultMaxItems = 10;
    public const int DefaultMaxAgeHours = 48;
    public const int MaxEntriesForSummary = 20;

    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(15);

    // Only used in Development when Digest:Feeds is empty: the demo must work
    // out of the box without network access or configuration.
    private const string FakeSourceName = "Feed de ejemplo";
    private const string FakeSourceUrl = "fake://digest";

    public async Task<DigestEntity?> GenerateAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var maxAgeHours = configuration.GetValue("Digest:MaxAgeHours", DefaultMaxAgeHours);
        var maxItems = configuration.GetValue("Digest:MaxItems", DefaultMaxItems);

        var sources = ReadFeedSources();
        var fetcher = feedFetcher;

        if (sources.Count == 0)
        {
            if (environment.IsDevelopment())
            {
                logger.LogInformation(
                    "Digest:Feeds está vacío; se usa el feed de ejemplo de desarrollo.");
                sources = [new FeedSource(FakeSourceName, FakeSourceUrl)];
                fetcher = new FakeFeedFetcher();
            }
            else
            {
                logger.LogWarning(
                    "Digest:Feeds está vacío: no se puede generar el digest. " +
                    "Configurá Digest:Feeds con al menos un feed.");
                return null;
            }
        }

        var fetched = await Task.WhenAll(
            sources.Select(source => FetchFeedAsync(fetcher, source, ct)));

        var entries = SelectCandidates(
            fetched.SelectMany(batch => batch),
            now,
            maxAgeHours);

        if (entries.Count == 0)
        {
            logger.LogInformation(
                "Los feeds no devolvieron notas recientes: se conserva el digest anterior.");
            return null;
        }

        var proposal = await summarizer.SummarizeAsync(entries, ct);
        var clean = DigestValidator.Clean(proposal, entries, maxItems);

        if (clean.Items.Count == 0)
        {
            logger.LogWarning(
                "El resumen no produjo ítems válidos: se conserva el digest anterior.");
            return null;
        }

        return await PersistAsync(clean, DateOnly.FromDateTime(now.UtcDateTime), now, ct);
    }

    private List<FeedSource> ReadFeedSources() =>
        configuration.GetSection("Digest:Feeds")
            .GetChildren()
            .Select(section => new FeedSource(
                section["Name"]?.Trim() ?? "",
                section["Url"]?.Trim() ?? ""))
            .Where(source => source.Url.Length > 0)
            .ToList();

    private async Task<IReadOnlyList<FeedEntry>> FetchFeedAsync(
        IFeedFetcher fetcher,
        FeedSource source,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(FetchTimeout);

        try
        {
            return await fetcher.FetchAsync(source, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "El feed {Source} no respondió en {Seconds} s; se omite.",
                source.Name,
                FetchTimeout.TotalSeconds);
            return [];
        }
        catch (Exception exception)
        {
            // A broken feed must never abort the digest: the remaining feeds
            // still contribute notes.
            logger.LogWarning(
                exception,
                "No se pudo leer el feed {Source} ({Url}); se omite.",
                source.Name,
                source.Url);
            return [];
        }
    }

    /// <summary>
    /// Applies the age window, removes repeated notes (same URL or same
    /// normalized title) and keeps the newest entries for the summarizer.
    /// Entries without a publication date are trusted and ordered last.
    /// </summary>
    private static List<FeedEntry> SelectCandidates(
        IEnumerable<FeedEntry> entries,
        DateTimeOffset now,
        int maxAgeHours)
    {
        var cutoff = now.AddHours(-Math.Max(0, maxAgeHours));
        var seenUrls = new HashSet<string>(StringComparer.Ordinal);
        var seenTitles = new HashSet<string>(StringComparer.Ordinal);
        var selected = new List<FeedEntry>(MaxEntriesForSummary);

        foreach (var entry in entries
                     .OrderByDescending(item => item.PublishedAt.HasValue)
                     .ThenByDescending(item => item.PublishedAt))
        {
            if (string.IsNullOrWhiteSpace(entry.Url))
                continue;

            if (entry.PublishedAt is { } publishedAt && publishedAt < cutoff)
                continue;

            var url = entry.Url.Trim();
            if (!seenUrls.Add(url))
                continue;

            var titleKey = NormalizeTitle(entry.Title);
            if (titleKey.Length > 0 && !seenTitles.Add(titleKey))
                continue;

            selected.Add(entry with { Url = url });

            if (selected.Count == MaxEntriesForSummary)
                break;
        }

        return selected;
    }

    private static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "";

        var normalized = new StringBuilder(title.Length);
        var previousWasSpace = false;

        foreach (var character in title.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                    normalized.Append(' ');
                previousWasSpace = true;
                continue;
            }

            normalized.Append(char.ToLowerInvariant(character));
            previousWasSpace = false;
        }

        return normalized.ToString();
    }

    private async Task<DigestEntity> PersistAsync(
        DigestSummary clean,
        DateOnly date,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // The generator may run more than once on the same scoped context (two
        // refreshes, a retry): start from a clean tracker so a previous run's
        // entities cannot be mistaken for rows that still exist.
        db.ChangeTracker.Clear();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var digest = await db.Digests
            .FirstOrDefaultAsync(row => row.Date == date, ct);

        if (digest is null)
        {
            digest = new DigestEntity
            {
                Id = Guid.NewGuid(),
                Date = date
            };
            db.Digests.Add(digest);
        }
        else
        {
            // Delete the previous items first: the (DigestId, Url) index is
            // unique, so reinserting the same URL in one SaveChanges would
            // collide if EF ordered the inserts before the deletes.
            var previous = await db.DigestItems
                .Where(item => item.DigestId == digest.Id)
                .ToListAsync(ct);
            db.DigestItems.RemoveRange(previous);
            await db.SaveChangesAsync(ct);
            digest.Items = [];
        }

        digest.Summary = clean.Summary ?? "";
        digest.GeneratedAt = now;

        var newItems = new List<DigestItem>(clean.Items.Count);
        for (var index = 0; index < clean.Items.Count; index++)
        {
            var draft = clean.Items[index];
            var item = new DigestItem
            {
                Id = Guid.NewGuid(),
                DigestId = digest.Id,
                Title = draft.Title,
                Url = draft.Url,
                Source = draft.Source,
                Summary = draft.Summary,
                StyleTagsJson = JsonSerializer.Serialize(draft.StyleTags),
                ImageUrl = draft.ImageUrl,
                PublishedAt = draft.PublishedAt,
                SortOrder = index
            };

            digest.Items.Add(item);
            newItems.Add(item);
        }

        // State the intent explicitly: a new entity with an already assigned
        // Guid key that is only discovered through the navigation would be
        // inferred as Modified and fail with a concurrency exception.
        db.DigestItems.AddRange(newItems);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return digest;
    }
}
