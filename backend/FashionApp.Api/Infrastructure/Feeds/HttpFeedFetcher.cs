using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace FashionApp.Api.Infrastructure.Feeds;

/// <summary>
/// Downloads and parses RSS 2.0 and Atom feeds. The fetcher is deliberately
/// forgiving: a broken feed, a timeout or malformed XML produces an empty list
/// so the digest generator can keep working with the remaining feeds.
/// </summary>
public sealed class HttpFeedFetcher : IFeedFetcher
{
    public const int MaxItemsPerFeed = 15;
    public const int MaxSummaryLength = 400;

    private const string MediaNamespace = "http://search.yahoo.com/mrss/";
    private const string UserAgent = "FashionApp/1.0 (+https://github.com/fashion-app)";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private static readonly Regex HtmlTags = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;

    public HttpFeedFetcher(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = RequestTimeout;

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public async Task<IReadOnlyList<FeedEntry>> FetchAsync(
        FeedSource source,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(source.Url))
            return [];

        try
        {
            using var response = await _httpClient
                .GetAsync(source.Url, HttpCompletionOption.ResponseContentRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var xml = await response.Content
                .ReadAsStringAsync(ct)
                .ConfigureAwait(false);

            using var text = new StringReader(xml);
            using var reader = XmlReader.Create(text, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit
            });
            // The package exposes only the synchronous Load(XmlReader); the
            // network read above is already asynchronous.
            var feed = SyndicationFeed.Load(reader);
            if (feed is null)
                return [];

            var entries = new List<FeedEntry>(MaxItemsPerFeed);
            foreach (var item in feed.Items.Take(MaxItemsPerFeed))
            {
                var url = item.Links
                    // An enclosure is usually an image, not the article URL.
                    .Where(link => !string.Equals(
                        link.RelationshipType,
                        "enclosure",
                        StringComparison.OrdinalIgnoreCase))
                    .Select(link => link.Uri?.ToString())
                    .FirstOrDefault(link => !string.IsNullOrWhiteSpace(link));
                if (string.IsNullOrWhiteSpace(url))
                    continue;

                entries.Add(new FeedEntry(
                    Title: CleanText(item.Title?.Text, int.MaxValue) ?? "(sin título)",
                    Url: url.Trim(),
                    Source: string.IsNullOrWhiteSpace(source.Name) ? "Feed" : source.Name.Trim(),
                    PublishedAt: PublishedAt(item),
                    Summary: CleanText(item.Summary?.Text, MaxSummaryLength)
                        ?? CleanText((item.Content as TextSyndicationContent)?.Text, MaxSummaryLength),
                    ImageUrl: FindImage(item)));
            }

            return entries;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host shutdown or a cancelled request must not be reported as an
            // empty feed: it is not a provider failure.
            throw;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static DateTimeOffset? PublishedAt(SyndicationItem item)
    {
        if (item.PublishDate > DateTimeOffset.MinValue)
            return item.PublishDate;

        return item.LastUpdatedTime > DateTimeOffset.MinValue
            ? item.LastUpdatedTime
            : null;
    }

    /// <summary>
    /// Reads <c>media:content</c> / <c>media:thumbnail</c> element extensions
    /// (Media RSS) and, as a fallback, an image enclosure link.
    /// </summary>
    private static string? FindImage(SyndicationItem item)
    {
        foreach (var extension in item.ElementExtensions)
        {
            if (!string.Equals(
                    extension.OuterNamespace,
                    MediaNamespace,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (extension.OuterName is not ("content" or "thumbnail"))
                continue;

            try
            {
                var element = extension.GetObject<XElement>();
                var url = element.Attribute("url")?.Value;
                if (!string.IsNullOrWhiteSpace(url))
                    return url.Trim();
            }
            catch (Exception)
            {
                // A malformed extension must not discard the whole entry.
            }
        }

        return item.Links
            .FirstOrDefault(link =>
                string.Equals(link.RelationshipType, "enclosure", StringComparison.OrdinalIgnoreCase)
                && link.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
            ?.Uri
            ?.ToString();
    }

    /// <summary>
    /// Strips HTML tags, collapses whitespace and truncates to
    /// <paramref name="maxLength"/> characters.
    /// </summary>
    private static string? CleanText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = Whitespace
            .Replace(HtmlTags.Replace(value, " "), " ")
            .Trim();

        if (text.Length == 0)
            return null;

        if (maxLength != int.MaxValue && text.Length > maxLength)
            text = text[..maxLength].TrimEnd();

        return text;
    }
}
