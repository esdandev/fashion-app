using System.Net;
using System.Text;
using System.Text.Json;
using FashionApp.Api.Domain;
using FashionApp.Api.Features.Outfits;
using FashionApp.Api.Infrastructure.Ai;
using Microsoft.Extensions.Options;

public sealed class GeminiOutfitSuggesterTests
{
    private static readonly Garment Top = TestData.NewGarment("Camisa", GarmentCategory.Top);
    private static readonly Garment Bottom = TestData.NewGarment("Jean", GarmentCategory.Bottom);
    private static readonly Garment Shoes = TestData.NewGarment("Zapatillas", GarmentCategory.Shoes);
    private static readonly Garment[] Wardrobe = [Top, Bottom, Shoes];

    [Fact]
    public async Task Gemini_parses_a_plain_json_answer_into_drafts()
    {
        var answer = Answer(new
        {
            name = "Look de oficina",
            itemIds = new[] { Top.Id, Bottom.Id, Shoes.Id },
            occasion = "trabajo",
            rationale = "Camisa, jean y zapatillas para un clima templado."
        });
        var suggester = CreateSuggester(new StubHttpHandler(Envelope(answer)));

        var drafts = await suggester.SuggestAsync(Context(), default);

        var draft = Assert.Single(drafts);
        Assert.Equal("Look de oficina", draft.Name);
        Assert.Equal([Top.Id, Bottom.Id, Shoes.Id], draft.GarmentIds);
        Assert.Equal("trabajo", draft.Occasion);
        Assert.Equal("Camisa, jean y zapatillas para un clima templado.", draft.Rationale);
    }

    [Fact]
    public async Task Gemini_parses_a_fully_fenced_json_answer()
    {
        var answer = "```json\n" + Answer(new
        {
            name = "Look con vestido",
            itemIds = new[] { Top.Id, Bottom.Id, Shoes.Id },
            occasion = "cena",
            rationale = "Propuesta en bloque de código."
        }) + "\n```";
        var suggester = CreateSuggester(new StubHttpHandler(Envelope(answer)));

        var drafts = await suggester.SuggestAsync(Context(), default);

        var draft = Assert.Single(drafts);
        Assert.Equal("Look con vestido", draft.Name);
        Assert.Equal("cena", draft.Occasion);
    }

    [Fact]
    public async Task Gemini_returns_empty_when_the_answer_is_not_json()
    {
        var suggester = CreateSuggester(
            new StubHttpHandler(Envelope("no soy un objeto json")));

        var drafts = await suggester.SuggestAsync(Context(), default);

        Assert.Empty(drafts);
    }

    [Fact]
    public async Task Gemini_returns_empty_when_the_answer_has_no_outfits_array()
    {
        var suggester = CreateSuggester(
            new StubHttpHandler(Envelope("""{"otraCosa":true}""")));

        var drafts = await suggester.SuggestAsync(Context(), default);

        Assert.Empty(drafts);
    }

    [Fact]
    public async Task Gemini_reads_response_property_names_case_insensitively()
    {
        var answer = $$"""
            {
              "OutFits": [
                {
                  "NAME": "Look informal",
                  "ItemIds": ["{{Top.Id}}", "{{Bottom.Id}}", "{{Shoes.Id}}"],
                  "OCCASION": "casual",
                  "RATIONALE": "Cómodo para el día."
                }
              ]
            }
            """;
        var suggester = CreateSuggester(new StubHttpHandler(Envelope(answer)));

        var drafts = await suggester.SuggestAsync(Context(), default);

        var draft = Assert.Single(drafts);
        Assert.Equal("Look informal", draft.Name);
        Assert.Equal("casual", draft.Occasion);
        Assert.Equal("Cómodo para el día.", draft.Rationale);
        Assert.Equal([Top.Id, Bottom.Id, Shoes.Id], draft.GarmentIds);
    }

    [Fact]
    public async Task Gemini_ignores_item_ids_that_are_not_guids()
    {
        var answer = Answer(new
        {
            name = "Con ids inválidos",
            itemIds = new[]
            {
                Top.Id.ToString(),
                "no-es-un-guid",
                Bottom.Id.ToString(),
                ""
            },
            occasion = "trabajo",
            rationale = "Solo los ids parseables sobreviven."
        });
        var suggester = CreateSuggester(new StubHttpHandler(Envelope(answer)));

        var drafts = await suggester.SuggestAsync(Context(), default);

        var draft = Assert.Single(drafts);
        Assert.Equal([Top.Id, Bottom.Id], draft.GarmentIds);
    }

    [Fact]
    public async Task Gemini_mixed_draft_is_dropped_entirely_by_the_validator()
    {
        var invented = Guid.NewGuid();
        var answer = Answer(new
        {
            name = "Mezcla",
            itemIds = new[] { Top.Id, invented, Shoes.Id },
            occasion = "trabajo",
            rationale = "Un id real y uno inventado."
        });
        var suggester = CreateSuggester(new StubHttpHandler(Envelope(answer)));

        var drafts = await suggester.SuggestAsync(Context(), default);

        // Gemini passes the ids through untrusted; the validator is the trust
        // boundary. The chosen behaviour is all-or-nothing: a draft with any
        // unknown id is dropped, it is not repaired by removing that id.
        var draft = Assert.Single(drafts);
        Assert.Contains(invented, draft.GarmentIds);
        Assert.Empty(OutfitSuggestionValidator.KeepValid(drafts, Wardrobe));
    }

    [Fact]
    public async Task Gemini_preserves_repeated_ids_so_the_validator_discards_the_whole_proposal()
    {
        var answer = Answer(
            new
            {
                name = "Repetido",
                itemIds = new[] { Top.Id, Top.Id, Bottom.Id, Shoes.Id },
                occasion = "trabajo",
                rationale = "La misma prenda aparece dos veces."
            },
            new
            {
                name = "Válido",
                itemIds = new[] { Top.Id, Bottom.Id, Shoes.Id },
                occasion = "trabajo",
                rationale = "Esta propuesta sí cumple las reglas."
            });
        var suggester = CreateSuggester(new StubHttpHandler(Envelope(answer)));

        var drafts = await suggester.SuggestAsync(Context(), default);

        // The parser does not repair the answer: it keeps both occurrences so
        // the validator can apply the shared "no repeated garment" rule.
        Assert.Equal(2, drafts.Count);
        Assert.Equal([Top.Id, Top.Id, Bottom.Id, Shoes.Id], drafts[0].GarmentIds);

        var kept = OutfitSuggestionValidator.KeepValid(drafts, Wardrobe);

        var only = Assert.Single(kept);
        Assert.Equal("Válido", only.Name);
    }

    [Fact]
    public async Task Gemini_sends_the_expected_payload_and_prompt_context()
    {
        var answer = Answer(new
        {
            name = "Look de oficina",
            itemIds = new[] { Top.Id, Bottom.Id, Shoes.Id },
            occasion = "trabajo",
            rationale = "Formal."
        });
        var handler = new CapturingHttpHandler(answer);
        var suggester = CreateSuggester(handler);

        await suggester.SuggestAsync(Context(), default);

        Assert.NotNull(handler.Body);
        using var payload = JsonDocument.Parse(handler.Body!);
        var root = payload.RootElement;

        var generationConfig = root.GetProperty("generationConfig");
        Assert.Equal(
            "application/json",
            generationConfig.GetProperty("responseMimeType").GetString());
        Assert.Equal(0.4, generationConfig.GetProperty("temperature").GetDouble());

        var prompt = root
            .GetProperty("contents")[0]
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();
        Assert.NotNull(prompt);
        Assert.Contains("trabajo", prompt);
        Assert.Contains("templado", prompt);
        Assert.Contains(Top.Id.ToString(), prompt);
        Assert.Contains(Bottom.Id.ToString(), prompt);
        Assert.Contains(Shoes.Id.ToString(), prompt);
        Assert.Contains("\"formality\":3", prompt);

        Assert.NotNull(handler.RequestUri);
        Assert.Contains("gemini-2.0-flash", handler.RequestUri!.ToString());
        Assert.Contains("key=test", handler.RequestUri.ToString());
    }

    [Fact]
    public async Task Gemini_returns_empty_when_the_transport_fails()
    {
        var suggester = CreateSuggester(new ThrowingHttpHandler());

        var drafts = await suggester.SuggestAsync(Context(), default);

        Assert.Empty(drafts);
    }

    private static GeminiOutfitSuggester CreateSuggester(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            Options.Create(new AiOptions { ApiKey = "test" }));

    private static OutfitSuggestionContext Context() => new(
        "trabajo",
        "templado",
        Wardrobe.Select(ToSummary).ToList());

    private static GarmentSummary ToSummary(Garment garment) => new(
        garment.Id,
        garment.Category.ToString().ToLowerInvariant(),
        garment.ColorName,
        garment.Formality,
        garment.Seasons.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        []);

    private static string Answer(params object[] outfits) =>
        JsonSerializer.Serialize(new { outfits });

    private static string Envelope(string answer) => JsonSerializer.Serialize(new
    {
        candidates = new[]
        {
            new { content = new { parts = new[] { new { text = answer } } } }
        }
    });

    private sealed class CapturingHttpHandler(string answer) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            RequestUri = request.RequestUri;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    Envelope(answer),
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("El proveedor de IA no está disponible.");
    }
}
