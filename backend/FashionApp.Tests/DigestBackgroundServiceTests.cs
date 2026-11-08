using FashionApp.Api.Features.Digest;
using FashionApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using DigestEntity = FashionApp.Api.Domain.Digest;

public sealed class DigestBackgroundServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DelayUntilNextRun_waits_until_today_before_the_run_time()
    {
        var clock = new ManualTimeProvider(Now.AddHours(-4));

        var delay = DigestBackgroundService.DelayUntilNextRun(clock, new TimeOnly(7, 0));

        Assert.Equal(TimeSpan.FromHours(1), delay);
    }

    [Fact]
    public void DelayUntilNextRun_waits_until_tomorrow_after_the_run_time()
    {
        var clock = new ManualTimeProvider(Now.AddHours(-2));

        var delay = DigestBackgroundService.DelayUntilNextRun(clock, new TimeOnly(7, 0));

        Assert.Equal(TimeSpan.FromHours(23), delay);
    }

    [Fact]
    public void DelayUntilNextRun_at_the_run_time_waits_a_full_day()
    {
        var clock = new ManualTimeProvider(Now.AddHours(-3));

        var delay = DigestBackgroundService.DelayUntilNextRun(clock, new TimeOnly(7, 0));

        Assert.Equal(TimeSpan.FromDays(1), delay);
    }

    [Fact]
    public async Task Startup_skips_generation_when_todays_digest_already_exists()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.SeedDigestAsync(DateOnly.FromDateTime(Now.UtcDateTime));

        await harness.Service.RunStartupAsync(default);

        Assert.Equal(0, harness.Generator.Calls);
    }

    [Fact]
    public async Task Startup_generates_when_todays_digest_is_missing()
    {
        await using var harness = await Harness.CreateAsync();

        await harness.Service.RunStartupAsync(default);

        Assert.Equal(1, harness.Generator.Calls);
    }

    [Fact]
    public async Task Startup_swallows_generator_exceptions_so_the_host_survives()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Generator.Throw = true;

        var exception = await Record.ExceptionAsync(
            () => harness.Service.RunStartupAsync(default));

        Assert.Null(exception);
        Assert.Equal(1, harness.Generator.Calls);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private Harness()
        {
            var dbPath = Path.Combine(
                Path.GetTempPath(),
                "fashion-tests",
                Guid.NewGuid().ToString("N"),
                "digest-background.db");
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Digest:GenerateOnStartup"] = "true",
                    ["Digest:RunAt"] = "07:00"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(
                options => options.UseSqlite($"Data Source={dbPath}"));
            services.AddSingleton<IDigestGenerator>(Generator);

            Provider = services.BuildServiceProvider();
            Service = new DigestBackgroundService(
                Provider.GetRequiredService<IServiceScopeFactory>(),
                configuration,
                Clock,
                NullLogger<DigestBackgroundService>.Instance);
        }

        public ManualTimeProvider Clock { get; } = new(Now);

        public StubDigestGenerator Generator { get; } = new();

        public ServiceProvider Provider { get; }

        public DigestBackgroundService Service { get; }

        public static async Task<Harness> CreateAsync()
        {
            var harness = new Harness();
            await using var scope = harness.Provider.CreateAsyncScope();
            await scope.ServiceProvider
                .GetRequiredService<AppDbContext>()
                .Database
                .MigrateAsync();
            return harness;
        }

        public async Task SeedDigestAsync(DateOnly date)
        {
            await using var scope = Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Digests.Add(new DigestEntity
            {
                Id = Guid.NewGuid(),
                Date = date,
                Summary = "Anterior",
                GeneratedAt = Clock.GetUtcNow()
            });
            await db.SaveChangesAsync();
        }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private sealed class StubDigestGenerator : IDigestGenerator
    {
        public int Calls { get; private set; }

        public bool Throw { get; set; }

        public Task<DigestEntity?> GenerateAsync(CancellationToken ct)
        {
            Calls++;

            if (Throw)
                throw new InvalidOperationException("El proveedor no está disponible.");

            return Task.FromResult<DigestEntity?>(null);
        }
    }
}
