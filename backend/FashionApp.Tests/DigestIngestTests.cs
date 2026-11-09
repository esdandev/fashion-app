using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FashionApp.Api.Features.Digest;
using FashionApp.Api.Infrastructure.Feeds;

public sealed class DigestIngestTests
{
    // ---- Validator -------------------------------------------------------

    [Fact]
    public void Validator_drops_urls_not_present_in_the_feeds_and_unknown_tags()
    {
        var sources = new[] { Entry("Nota real", "https://example.com/real") };
        var summary = new DigestSummary("Resumen", [
            new DigestDraft("Buena", "https://example.com/real", "Vogue", "Resumen", ["minimalista", "inventado"], null, null),
            new DigestDraft("Inventada", "https://example.com/falsa", "Vogue", "Resumen", ["casual"], null, null),
        ]);

        var clean = DigestValidator.Clean(summary, sources, maxItems: 10);
        var item = Assert.Single(clean.Items);
        Assert.Equal("https://example.com/real", item.Url);
        Assert.Equal(["minimalista"], item.StyleTags);
    }

    [Fact]
    public void Validator_caps_items_and_trims_text()
    {
        var sources = Enumerable.Range(0, 15).Select(i => Entry($"N{i}", $"https://example.com/{i}")).ToArray();
        var summary = new DigestSummary(null, sources.Select(e =>
            new DigestDraft(new string('t', 300), e.Url, "Vogue", new string('s', 900), ["casual"], null, null)).ToList());

        var clean = DigestValidator.Clean(summary, sources, maxItems: 10);
        Assert.Equal(10, clean.Items.Count);
        Assert.Equal(200, clean.Items[0].Title.Length);
        Assert.Equal(600, clean.Items[0].Summary.Length);
    }

    [Fact]
    public void Validator_dedupes_by_url_and_orders_by_published_date_descending()
    {
        var older = new DateTimeOffset(2026, 11, 7, 8, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2026, 11, 8, 8, 0, 0, TimeSpan.Zero);
        var sources = new[]
        {
            Entry("Vieja", "https://example.com/vieja", older),
            Entry("Nueva", "https://example.com/nueva", newer)
        };
        var summary = new DigestSummary("Resumen", [
            new DigestDraft("Vieja", "https://example.com/vieja", "Vogue", "S", ["casual"], null, older),
            new DigestDraft("Vieja repetida", "https://example.com/vieja", "Vogue", "S", ["casual"], null, older),
            new DigestDraft("Nueva", "https://example.com/nueva", "Vogue", "S", ["casual"], null, newer),
            new DigestDraft("Sin fecha", "https://example.com/vieja", "Vogue", "S", ["casual"], null, null)
        ]);

        var clean = DigestValidator.Clean(summary, sources, maxItems: 10);

        Assert.Equal(new[] { "Nueva", "Vieja" }, clean.Items.Select(item => item.Title));
    }

    [Fact]
    public void Validator_caps_valid_tags_at_three()
    {
        var sources = new[] { Entry("Con muchos tags", "https://example.com/tags") };
        var summary = new DigestSummary(null, [
            new DigestDraft("Con muchos tags", "https://example.com/tags", "Vogue", "S",
                ["casual", "minimalista", "streetwear", "formal"], null, null)
        ]);

        var clean = DigestValidator.Clean(summary, sources, maxItems: 10);

        var item = Assert.Single(clean.Items);
        Assert.Equal(["casual", "minimalista", "streetwear"], item.StyleTags);
    }

    [Fact]
    public void Validator_drops_drafts_with_a_blank_title_or_summary()
    {
        var sources = new[]
        {
            Entry("Sin titulo", "https://example.com/no-title"),
            Entry("Sin resumen", "https://example.com/no-summary"),
            Entry("Completa", "https://example.com/complete")
        };
        var summary = new DigestSummary(null, [
            new DigestDraft("   ", "https://example.com/no-title", "Vogue", "Resumen válido.",
                ["casual"], null, null),
            new DigestDraft("Sin resumen", "https://example.com/no-summary", "Vogue", "  ",
                ["casual"], null, null),
            new DigestDraft("Completa", "https://example.com/complete", "Vogue", "Resumen válido.",
                ["casual"], null, null)
        ]);

        var clean = DigestValidator.Clean(summary, sources, maxItems: 10);

        var item = Assert.Single(clean.Items);
        Assert.Equal("https://example.com/complete", item.Url);
        Assert.Equal("Completa", item.Title);
        Assert.Equal("Resumen válido.", item.Summary);
    }

    [Fact]
    public void Validator_drops_drafts_without_a_single_valid_tag()
    {
        var sources = new[]
        {
            Entry("Con tag valido", "https://example.com/valid"),
            Entry("Sin tags validos", "https://example.com/no-tags"),
            Entry("Sin tags", "https://example.com/empty-tags")
        };
        var summary = new DigestSummary(null, [
            new DigestDraft("Con tag valido", "https://example.com/valid", "Vogue", "S",
                ["casual"], null, null),
            new DigestDraft("Sin tags validos", "https://example.com/no-tags", "Vogue", "S",
                ["inventado", "otro"], null, null),
            new DigestDraft("Sin tags", "https://example.com/empty-tags", "Vogue", "S",
                [], null, null)
        ]);

        var clean = DigestValidator.Clean(summary, sources, maxItems: 10);

        var item = Assert.Single(clean.Items);
        Assert.Equal("https://example.com/valid", item.Url);
        Assert.Equal(["casual"], item.StyleTags);
    }

    // ---- Feed fetchers ---------------------------------------------------

    [Fact]
    public async Task Fake_feed_fetcher_returns_eight_deterministic_fashion_entries()
    {
        var fetcher = new FakeFeedFetcher();

        var entries = await fetcher.FetchAsync(new FeedSource("Vogue", "https://example.com/feed"), default);

        Assert.Equal(8, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.StartsWith("https://", entry.Url);
            Assert.False(string.IsNullOrWhiteSpace(entry.Title));
            Assert.False(string.IsNullOrWhiteSpace(entry.Source));
            Assert.NotNull(entry.PublishedAt);
        });
        Assert.Equal(
            DemoFeedNotes.All.Select(note => note.Url),
            entries.Select(entry => entry.Url));
        Assert.DoesNotContain(entries, entry => entry.Url.Contains("example.com"));
    }

    [Fact]
    public async Task Http_feed_fetcher_resolves_relative_links_against_the_feed_url()
    {
        const string rss = """
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0">
              <channel>
                <title>Vogue</title>
                <link>https://www.vogue.com/feed</link>
                <item>
                  <title>Nota relativa</title>
                  <link>/fashion/nota-relativa</link>
                </item>
                <item>
                  <title>Nota inválida</title>
                  <link>javascript:alert(1)</link>
                </item>
              </channel>
            </rss>
            """;
        var fetcher = CreateFetcher(rss);

        var entries = await fetcher.FetchAsync(
            new FeedSource("Vogue", "https://www.vogue.com/feed"),
            default);

        var entry = Assert.Single(entries);
        Assert.Equal("https://www.vogue.com/fashion/nota-relativa", entry.Url);
    }

    [Fact]
    public async Task Http_feed_fetcher_parses_rss_with_dates_summaries_and_media_images()
    {
        const string rss = """
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0" xmlns:media="http://search.yahoo.com/mrss/">
              <channel>
                <title>Vogue</title>
                <item>
                  <title>Nota real</title>
                  <link>https://example.com/real</link>
                  <pubDate>Sun, 08 Nov 2026 08:00:00 GMT</pubDate>
                  <description>Un resumen de la nota.</description>
                  <media:content url="https://example.com/img.jpg" medium="image" />
                </item>
              </channel>
            </rss>
            """;
        var fetcher = CreateFetcher(rss);

        var entries = await fetcher.FetchAsync(new FeedSource("Vogue", "https://feed.test/rss"), default);

        var entry = Assert.Single(entries);
        Assert.Equal("Nota real", entry.Title);
        Assert.Equal("https://example.com/real", entry.Url);
        Assert.Equal("Vogue", entry.Source);
        Assert.Equal("Un resumen de la nota.", entry.Summary);
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 8, 0, 0, TimeSpan.Zero), entry.PublishedAt);
        Assert.Equal("https://example.com/img.jpg", entry.ImageUrl);
    }

    [Fact]
    public async Task Http_feed_fetcher_parses_atom_entries()
    {
        const string atom = """
            <?xml version="1.0" encoding="utf-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Elle</title>
              <id>urn:elle</id>
              <updated>2026-11-08T08:00:00Z</updated>
              <entry>
                <title>Nota atom</title>
                <link href="https://example.com/atom" />
                <updated>2026-11-08T08:00:00Z</updated>
                <summary>Resumen atom.</summary>
              </entry>
            </feed>
            """;
        var fetcher = CreateFetcher(atom);

        var entries = await fetcher.FetchAsync(new FeedSource("Elle", "https://feed.test/atom"), default);

        var entry = Assert.Single(entries);
        Assert.Equal("Nota atom", entry.Title);
        Assert.Equal("https://example.com/atom", entry.Url);
        Assert.Equal("Elle", entry.Source);
        Assert.Equal("Resumen atom.", entry.Summary);
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 8, 0, 0, TimeSpan.Zero), entry.PublishedAt);
    }

    [Fact]
    public async Task Http_feed_fetcher_honors_the_xml_declaration_encoding()
    {
        const string rss = """
            <?xml version="1.0" encoding="ISO-8859-1"?>
            <rss version="2.0">
              <channel>
                <title>Vogue España</title>
                <item>
                  <title>Otoño cálido</title>
                  <link>https://example.com/otono</link>
                  <description>Algodón, lino y accesorios para la temporada.</description>
                </item>
              </channel>
            </rss>
            """;
        var fetcher = new HttpFeedFetcher(
            new HttpClient(new BytesHttpHandler(Encoding.Latin1.GetBytes(rss))));

        var entries = await fetcher.FetchAsync(
            new FeedSource("Vogue", "https://feed.test/latin1"),
            default);

        var entry = Assert.Single(entries);
        Assert.Equal("Otoño cálido", entry.Title);
        Assert.Equal("Algodón, lino y accesorios para la temporada.", entry.Summary);
    }

    [Fact]
    public async Task Http_feed_fetcher_caps_items_at_fifteen_and_summaries_at_four_hundred_chars()
    {
        var items = string.Join("", Enumerable.Range(0, 20).Select(i => $"""
            <item>
              <title>N{i}</title>
              <link>https://example.com/{i}</link>
              <description>{new string('s', 500)}</description>
            </item>
            """));
        var rss = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0"><channel><title>Feed</title>{items}</channel></rss>
            """;
        var fetcher = CreateFetcher(rss);

        var entries = await fetcher.FetchAsync(new FeedSource("Vogue", "https://feed.test/rss"), default);

        Assert.Equal(15, entries.Count);
        Assert.All(entries, entry => Assert.Equal(400, entry.Summary!.Length));
    }

    [Fact]
    public async Task Http_feed_fetcher_returns_empty_on_invalid_xml_and_transport_errors()
    {
        var invalidXml = CreateFetcher("<rss><channel>");
        var throwing = new HttpFeedFetcher(new HttpClient(new ThrowingHttpHandler()));

        Assert.Empty(await invalidXml.FetchAsync(new FeedSource("Vogue", "https://feed.test/rss"), default));
        Assert.Empty(await throwing.FetchAsync(new FeedSource("Vogue", "https://feed.test/rss"), default));
    }

    // ---- Summarizer ------------------------------------------------------

    [Fact]
    public async Task Rule_based_summarizer_keeps_titles_and_infers_tags()
    {
        var entries = new[]
        {
            Entry("Tendencias minimalistas", "https://example.com/1",
                summary: "Lino, algodón y siluetas esenciales."),
            Entry("Streetwear urbano", "https://example.com/2",
                summary: "Hoodies, denim y zapatillas.")
        };

        var result = await new RuleBasedDigestSummarizer()
            .SummarizeAsync(entries, default);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Tendencias minimalistas", result.Items[0].Title);
        Assert.Contains("minimalista", result.Items[0].StyleTags);
        Assert.Contains("streetwear", result.Items[1].StyleTags);
        Assert.Contains("2 novedades", result.Summary);
    }

    // ---- Helpers ---------------------------------------------------------

    private static FeedEntry Entry(
        string title,
        string url,
        DateTimeOffset? publishedAt = null,
        string? summary = null,
        string? imageUrl = null,
        string source = "Vogue") =>
        new(title, url, source, publishedAt, summary, imageUrl);

    private static HttpFeedFetcher CreateFetcher(string body) =>
        new(new HttpClient(new StubHttpHandler(body)));

    private sealed class BytesHttpHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body)
            };
            response.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/rss+xml");

            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("El proveedor no está disponible.");
    }
}
