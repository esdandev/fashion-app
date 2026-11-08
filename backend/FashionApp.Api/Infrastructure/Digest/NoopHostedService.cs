using Microsoft.Extensions.Hosting;

namespace FashionApp.Api.Infrastructure;

/// <summary>
/// Placeholder registered as the only hosted service in demo mode. It keeps the
/// host startup contract intact without constructing or starting the digest
/// job, which would fetch feeds and call the AI provider.
/// </summary>
internal sealed class NoopHostedService : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
