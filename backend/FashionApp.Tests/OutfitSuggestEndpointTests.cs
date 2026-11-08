using System.Net;
using System.Net.Http.Json;
using FashionApp.Api.Domain;
using FashionApp.Api.Features.Outfits;
using FashionApp.Api.Infrastructure;
using FashionApp.Api.Infrastructure.Ai;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public sealed class OutfitSuggestEndpointTests : IDisposable
{
    private const string SuggestionKind = "suggestion";

    private readonly TestAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Suggest_returns_valid_outfits_and_consumes_suggestion_quota()
    {
        await SeedWardrobe(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/outfits/suggest",
            new { occasion = "trabajo", weather = "templado" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<SuggestOutfitsResponse>();
        Assert.NotNull(body);
        Assert.NotEmpty(body!.Outfits);

        var garments = await ActiveGarmentsAsync(factory);
        foreach (var draft in body.Outfits)
        {
            Assert.NotEmpty(draft.GarmentIds);
            var resolved = draft.GarmentIds
                .Select(id => garments.Single(garment => garment.Id == id))
                .ToList();
            Assert.True(
                OutfitRules.Validate(resolved).IsValid,
                $"La propuesta '{draft.Name}' no cumple las reglas.");
        }

        Assert.Equal(1, await SuggestionCountAsync(factory));
        Assert.Equal(0, await AnalysisCountAsync(factory));
    }

    [Fact]
    public async Task Suggest_returns_two_stable_outfits_when_the_wardrobe_allows_it()
    {
        await TestData.SeedGarments(
            factory,
            TestData.NewGarment("Camisa", GarmentCategory.Top),
            TestData.NewGarment("Camisa negra", GarmentCategory.Top),
            TestData.NewGarment("Jean", GarmentCategory.Bottom),
            TestData.NewGarment("Zapatillas", GarmentCategory.Shoes));
        var client = factory.CreateClient();

        var first = await PostSuggestAsync(client, "trabajo");
        var second = await PostSuggestAsync(client, "trabajo");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<SuggestOutfitsResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<SuggestOutfitsResponse>();
        Assert.Equal(2, firstBody!.Outfits.Count);
        Assert.Equal(
            firstBody.Outfits.Select(o => string.Join(",", o.GarmentIds)),
            secondBody!.Outfits.Select(o => string.Join(",", o.GarmentIds)));
        Assert.Equal(1, await SuggestionCountAsync(factory));
    }

    [Fact]
    public async Task Suggest_uses_the_suggestion_limit_not_the_analysis_limit()
    {
        using var limited = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Ai:DailyAnalysisLimit"] = "0",
                    ["Ai:DailySuggestionLimit"] = "5"
                })));
        await SeedWardrobe(factory);
        var client = limited.CreateClient();

        var first = await PostSuggestAsync(client, "trabajo");
        var second = await PostSuggestAsync(client, "cena");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, await SuggestionCountAsync(factory));
    }

    [Fact]
    public async Task Suggest_returns_429_when_suggestion_quota_is_exhausted()
    {
        using var limited = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                    { ["Ai:DailySuggestionLimit"] = "1" })));
        await SeedWardrobe(factory);
        var client = limited.CreateClient();

        // Different occasions so the second request is a cache miss and reaches
        // the quota check instead of being served from the first response.
        var first = await PostSuggestAsync(client, "trabajo");
        var second = await PostSuggestAsync(client, "cena");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.True(
            second.Headers.TryGetValues("Retry-After", out var retryAfter),
            "La respuesta 429 no incluye Retry-After.");
        Assert.True(int.TryParse(Assert.Single(retryAfter), out var seconds));
        Assert.InRange(seconds, 1, 86_400);

        Assert.Equal(1, await SuggestionCountAsync(factory));
    }

    [Fact]
    public async Task Suggest_uses_cache_without_consuming_more_quota_or_calling_the_ai()
    {
        using var counting = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOutfitSuggester>();
                services.AddSingleton<IOutfitSuggester, CountingSuggester>();
            }));
        await SeedWardrobe(factory);
        var client = counting.CreateClient();

        var first = await PostSuggestAsync(client, "trabajo");
        var second = await PostSuggestAsync(client, "trabajo");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<SuggestOutfitsResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<SuggestOutfitsResponse>();
        Assert.Equal(
            firstBody!.Outfits.Select(o => o.Name),
            secondBody!.Outfits.Select(o => o.Name));
        Assert.Equal(
            firstBody.Outfits.Select(o => string.Join(",", o.GarmentIds)),
            secondBody.Outfits.Select(o => string.Join(",", o.GarmentIds)));

        var suggester = Assert.IsType<CountingSuggester>(
            counting.Services.GetRequiredService<IOutfitSuggester>());
        Assert.Equal(1, suggester.Calls);
        Assert.Equal(1, await SuggestionCountAsync(factory));
    }

    [Fact]
    public async Task Suggest_keeps_the_valid_draft_and_drops_the_invented_one()
    {
        using var mixed = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOutfitSuggester>();
                services.AddSingleton<IOutfitSuggester, MixedSuggester>();
            }));
        await SeedWardrobe(factory);

        var response = await PostSuggestAsync(mixed.CreateClient(), "trabajo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SuggestOutfitsResponse>();
        var only = Assert.Single(body!.Outfits);
        Assert.Equal("Válido", only.Name);
        var wardrobeIds = (await ActiveGarmentsAsync(factory)).Select(g => g.Id).ToHashSet();
        Assert.All(only.GarmentIds, id => Assert.Contains(id, wardrobeIds));
        Assert.Equal(1, await SuggestionCountAsync(factory));
    }

    [Fact]
    public async Task Suggest_retries_once_and_returns_the_second_call_proposal()
    {
        using var flaky = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOutfitSuggester>();
                services.AddSingleton<IOutfitSuggester, FlakySuggester>();
            }));
        await SeedWardrobe(factory);

        var response = await PostSuggestAsync(flaky.CreateClient(), "trabajo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SuggestOutfitsResponse>();
        var only = Assert.Single(body!.Outfits);
        // The rule-based fallback names its drafts "Look de dos piezas"; seeing
        // the retry proposal proves the second call was used, not the fallback.
        Assert.Equal(FlakySuggester.RetryName, only.Name);
        Assert.Equal("propuesta del reintento", only.Rationale);

        var suggester = Assert.IsType<FlakySuggester>(
            flaky.Services.GetRequiredService<IOutfitSuggester>());
        Assert.Equal(2, suggester.Calls);
        Assert.Equal(1, await SuggestionCountAsync(factory));
    }

    [Fact]
    public async Task Suggest_falls_back_to_rules_when_the_ai_fails()
    {
        using var failing = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOutfitSuggester>();
                services.AddSingleton<IOutfitSuggester, ThrowingSuggester>();
            }));
        await SeedWardrobe(factory);

        var response = await PostSuggestAsync(failing.CreateClient(), "trabajo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SuggestOutfitsResponse>();
        Assert.NotEmpty(body!.Outfits);

        var garments = await ActiveGarmentsAsync(factory);
        foreach (var draft in body.Outfits)
        {
            var resolved = draft.GarmentIds
                .Select(id => garments.Single(garment => garment.Id == id))
                .ToList();
            Assert.True(OutfitRules.Validate(resolved).IsValid);
        }

        var suggester = Assert.IsType<ThrowingSuggester>(
            failing.Services.GetRequiredService<IOutfitSuggester>());
        Assert.Equal(2, suggester.Calls);
        Assert.Equal(1, await SuggestionCountAsync(factory));
    }

    [Fact]
    public async Task Suggest_normalizes_an_overlong_gemini_answer_into_saveable_drafts()
    {
        using var overlong = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOutfitSuggester>();
                services.AddSingleton<IOutfitSuggester, OverlongSuggester>();
            }));
        await TestData.SeedGarments(
            factory,
            TestData.NewGarment("Camisa", GarmentCategory.Top),
            TestData.NewGarment("Camisa negra", GarmentCategory.Top),
            TestData.NewGarment("Jean", GarmentCategory.Bottom),
            TestData.NewGarment("Pantalón", GarmentCategory.Bottom),
            TestData.NewGarment("Zapatillas", GarmentCategory.Shoes),
            TestData.NewGarment("Campera", GarmentCategory.Outerwear),
            TestData.NewGarment("Cartera", GarmentCategory.Bag));

        var client = overlong.CreateClient();
        var response = await PostSuggestAsync(client, "trabajo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SuggestOutfitsResponse>();
        Assert.NotNull(body);
        Assert.Equal(3, body!.Outfits.Count);

        var normalized = body.Outfits[0];
        Assert.Equal(OutfitDraftNormalizer.MaxNameLength, normalized.Name.Length);
        Assert.Equal(OutfitDraftNormalizer.MaxOccasionLength, normalized.Occasion!.Length);
        Assert.Equal(OutfitDraftNormalizer.MaxRationaleLength, normalized.Rationale.Length);
        Assert.Equal(OutfitDraftNormalizer.MaxGarmentIds, normalized.GarmentIds.Length);

        Assert.All(body.Outfits, draft =>
        {
            Assert.InRange(draft.Name.Length, 1, OutfitDraftNormalizer.MaxNameLength);
            Assert.True(
                draft.Occasion is null
                || draft.Occasion.Length <= OutfitDraftNormalizer.MaxOccasionLength);
            Assert.InRange(
                draft.Rationale.Length,
                0,
                OutfitDraftNormalizer.MaxRationaleLength);
            Assert.InRange(
                draft.GarmentIds.Length,
                1,
                OutfitDraftNormalizer.MaxGarmentIds);
        });

        // Saving the normalized draft must not hit any create-endpoint limit.
        var save = await client.PostAsJsonAsync(
            "/api/outfits",
            new CreateOutfitRequest(
                normalized.Name,
                normalized.Occasion,
                normalized.GarmentIds,
                "ai",
                normalized.Rationale));

        Assert.Equal(HttpStatusCode.Created, save.StatusCode);
    }

    [Fact]
    public async Task Rate_limit_returns_429_problem_details_and_never_reaches_the_handler()
    {
        // Testing disables the limiter by default; this host opts back in with
        // a two-request window.
        using var limited = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:Enabled"] = "true",
                    ["RateLimiting:PermitLimit"] = "2"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOutfitSuggester>();
                services.AddSingleton<IOutfitSuggester, CountingSuggester>();
            });
        });
        await SeedWardrobe(factory);
        var client = limited.CreateClient();

        // Different occasions avoid the suggestion cache, so every accepted
        // request would call the suggester once.
        var first = await PostSuggestAsync(client, "trabajo");
        var second = await PostSuggestAsync(client, "cena");
        var third = await PostSuggestAsync(client, "casual");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal(
            "application/problem+json",
            third.Content.Headers.ContentType?.MediaType);

        var problem = await third.Content.ReadAsStringAsync();
        Assert.Contains("Demasiadas solicitudes", problem);
        Assert.Contains("límite de solicitudes por minuto", problem);

        var suggester = Assert.IsType<CountingSuggester>(
            limited.Services.GetRequiredService<IOutfitSuggester>());
        Assert.Equal(2, suggester.Calls);
    }

    [Theory]
    [InlineData("granizo")]
    [InlineData("otoño")]
    public async Task Suggest_rejects_an_unknown_weather_with_400(string weather)
    {
        await SeedWardrobe(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/outfits/suggest",
            new { occasion = "trabajo", weather });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(0, await TotalUsageCountAsync(factory));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Suggest_defaults_to_temperate_when_the_weather_is_missing(string? weather)
    {
        await SeedWardrobe(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/outfits/suggest",
            new { occasion = "trabajo", weather });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SuggestOutfitsResponse>();
        Assert.NotNull(body);
        Assert.NotEmpty(body!.Outfits);
        Assert.Equal(1, await SuggestionCountAsync(factory));
    }

    [Fact]
    public async Task Suggest_recomputes_when_the_wardrobe_changes()
    {
        using var counting = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOutfitSuggester>();
                services.AddSingleton<IOutfitSuggester, CountingSuggester>();
            }));
        await SeedWardrobe(factory);
        var client = counting.CreateClient();

        var first = await PostSuggestAsync(client, "trabajo");
        await TestData.SeedGarments(
            factory,
            TestData.NewGarment("Camisa extra", GarmentCategory.Top));
        var second = await PostSuggestAsync(client, "trabajo");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var suggester = Assert.IsType<CountingSuggester>(
            counting.Services.GetRequiredService<IOutfitSuggester>());
        Assert.Equal(2, suggester.Calls);
        Assert.Equal(2, await SuggestionCountAsync(factory));
    }

    [Fact]
    public async Task Suggest_requires_the_token()
    {
        using var personal = new TestAppFactory { Token = "secreto" };

        var response = await personal.CreateClient().PostAsJsonAsync(
            "/api/outfits/suggest",
            new { occasion = "trabajo", weather = "templado" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Suggest_is_blocked_in_demo()
    {
        using var demo = new TestAppFactory { Demo = true };

        var response = await demo.CreateClient().PostAsJsonAsync(
            "/api/outfits/suggest",
            new { occasion = "trabajo", weather = "templado" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static Task<HttpResponseMessage> PostSuggestAsync(
        HttpClient client,
        string occasion,
        string weather = "templado") =>
        client.PostAsJsonAsync("/api/outfits/suggest", new { occasion, weather });

    private static async Task SeedWardrobe(TestAppFactory factory) =>
        await TestData.SeedGarments(
            factory,
            TestData.NewGarment("Camisa", GarmentCategory.Top),
            TestData.NewGarment("Jean", GarmentCategory.Bottom),
            TestData.NewGarment("Zapatillas", GarmentCategory.Shoes));

    private static async Task<List<Garment>> ActiveGarmentsAsync(TestAppFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Garments
            .AsNoTracking()
            .Where(garment => !garment.IsArchived)
            .ToListAsync();
    }

    private static async Task<int> SuggestionCountAsync(TestAppFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UsageCounters
            .Where(counter => counter.Kind == SuggestionKind)
            .Select(counter => counter.Count)
            .SingleOrDefaultAsync();
    }

    private static async Task<int> AnalysisCountAsync(TestAppFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UsageCounters
            .Where(counter => counter.Kind == "analysis")
            .Select(counter => counter.Count)
            .SingleOrDefaultAsync();
    }

    private static async Task<int> TotalUsageCountAsync(TestAppFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UsageCounters.CountAsync();
    }

    private sealed class CountingSuggester : IOutfitSuggester
    {
        private readonly RuleBasedOutfitSuggester inner = new();

        public int Calls { get; private set; }

        public Task<IReadOnlyList<OutfitDraft>> SuggestAsync(
            OutfitSuggestionContext context,
            CancellationToken ct)
        {
            Calls++;
            return inner.SuggestAsync(context, ct);
        }
    }

    private sealed class MixedSuggester : IOutfitSuggester
    {
        private readonly RuleBasedOutfitSuggester inner = new();

        public async Task<IReadOnlyList<OutfitDraft>> SuggestAsync(
            OutfitSuggestionContext context,
            CancellationToken ct)
        {
            var valid = await inner.SuggestAsync(context, ct);
            var drafts = new List<OutfitDraft>();
            if (valid.Count > 0)
                drafts.Add(valid[0] with { Name = "Válido" });
            drafts.Add(new OutfitDraft(
                "Inventado",
                [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()],
                null,
                "ids que no existen en el guardarropa"));
            return drafts;
        }
    }

    private sealed class ThrowingSuggester : IOutfitSuggester
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<OutfitDraft>> SuggestAsync(
            OutfitSuggestionContext context,
            CancellationToken ct)
        {
            Calls++;
            throw new HttpRequestException("El proveedor de IA no está disponible.");
        }
    }

    /// <summary>
    /// Returns four distinct proposals with text and ids beyond the saveable
    /// limits, mimicking an overlong Gemini answer.
    /// </summary>
    private sealed class OverlongSuggester : IOutfitSuggester
    {
        public Task<IReadOnlyList<OutfitDraft>> SuggestAsync(
            OutfitSuggestionContext context,
            CancellationToken ct)
        {
            var tops = context.Wardrobe.Where(g => g.Category == "top").ToList();
            var bottoms = context.Wardrobe.Where(g => g.Category == "bottom").ToList();
            var shoes = context.Wardrobe.Single(g => g.Category == "shoes");
            var outerwear = context.Wardrobe.Single(g => g.Category == "outerwear");
            var bag = context.Wardrobe.Single(g => g.Category == "bag");

            var drafts = new List<OutfitDraft>
            {
                new(
                    new string('N', 300),
                    [
                        tops[0].Id,
                        bottoms[0].Id,
                        shoes.Id,
                        outerwear.Id,
                        bag.Id
                    ],
                    new string('O', 200),
                    new string('R', 1000))
            };

            drafts.Add(new OutfitDraft(
                "Alternativa 1", [tops[1].Id, bottoms[0].Id, shoes.Id], null, "ok"));
            drafts.Add(new OutfitDraft(
                "Alternativa 2", [tops[0].Id, bottoms[1].Id, shoes.Id], null, "ok"));
            drafts.Add(new OutfitDraft(
                "Alternativa 3", [tops[1].Id, bottoms[1].Id, shoes.Id], null, "ok"));

            return Task.FromResult<IReadOnlyList<OutfitDraft>>(drafts);
        }
    }

    /// <summary>
    /// Returns nothing on the first call and a valid proposal on the second, so
    /// the test can distinguish the service retry from the rule-based fallback.
    /// </summary>
    private sealed class FlakySuggester : IOutfitSuggester
    {
        public const string RetryName = "Propuesta del reintento";

        private readonly RuleBasedOutfitSuggester inner = new();

        public int Calls { get; private set; }

        public async Task<IReadOnlyList<OutfitDraft>> SuggestAsync(
            OutfitSuggestionContext context,
            CancellationToken ct)
        {
            Calls++;
            if (Calls == 1)
                return [];

            var valid = await inner.SuggestAsync(context, ct);
            return valid.Count == 0
                ? []
                : [valid[0] with { Name = RetryName, Rationale = "propuesta del reintento" }];
        }
    }
}
