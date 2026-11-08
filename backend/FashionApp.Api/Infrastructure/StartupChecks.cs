namespace FashionApp.Api.Infrastructure;

/// <summary>
/// Startup guards that turn dangerous deployments into clear Spanish errors
/// instead of a silently open personal instance or a demo pointed at real data.
/// </summary>
public static class StartupChecks
{
    public const string AllowAnonymousKey = "App:AllowAnonymous";

    /// <summary>
    /// A personal instance exposed in Production must require its shared token.
    /// Development and Testing stay open for local work, and an explicit
    /// <c>App:AllowAnonymous=true</c> escape hatch is honored for throwaway hosts.
    /// </summary>
    public static void ValidateSecurity(
        string environmentName,
        string? token,
        bool demoEnabled,
        bool allowAnonymous)
    {
        if (demoEnabled || allowAnonymous)
            return;

        if (!string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "La instancia personal en Production requiere App:Token. " +
                "Definí la variable APP__TOKEN o, solo para un entorno descartable, " +
                "activá App:AllowAnonymous=true.");
        }
    }
}
