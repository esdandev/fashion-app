namespace FashionApp.Api.Features.Digest;

/// <summary>
/// In-process cooldown for the admin refresh endpoint. AI calls cost money, so
/// even a leaked admin secret allows at most one regeneration per hour. The
/// guard is a singleton: the cooldown is shared by every request to the host.
/// </summary>
public sealed class DigestRefreshGuard(TimeProvider clock)
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromHours(1);

    private readonly object _gate = new();
    private DateTimeOffset? _last;

    /// <summary>Exposed so tests can advance their manual clock through the guard.</summary>
    public TimeProvider Clock { get; } = clock;

    /// <summary>
    /// Returns <c>true</c> and records the attempt when the cooldown has
    /// elapsed; returns <c>false</c> without changing state otherwise.
    /// </summary>
    public bool TryBegin()
    {
        lock (_gate)
        {
            var now = Clock.GetUtcNow();
            if (_last is { } last && now - last < Cooldown)
                return false;

            _last = now;
            return true;
        }
    }

    /// <summary>
    /// Time left before another refresh is allowed, or <c>null</c> when there
    /// is no active cooldown.
    /// </summary>
    public TimeSpan? Remaining()
    {
        lock (_gate)
        {
            if (_last is not { } last)
                return null;

            var remaining = Cooldown - (Clock.GetUtcNow() - last);
            return remaining > TimeSpan.Zero ? remaining : null;
        }
    }
}
