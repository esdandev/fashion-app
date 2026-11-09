namespace FashionApp.Api.Infrastructure;

/// <summary>
/// Minimal <c>.env</c> loader for local development. The app searches upward
/// from the given directories for a <c>.env</c> file and exports each
/// <c>KEY=VALUE</c> pair to the process environment, without overwriting
/// variables that are already set. Production deployments keep using real
/// environment variables/secrets.
/// </summary>
public static class EnvFile
{
    private const int MaxParentLevels = 8;

    public static void Load(params string[] startDirectories)
    {
        foreach (var start in startDirectories)
        {
            if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start))
                continue;

            var directory = new DirectoryInfo(start);
            for (var level = 0; level < MaxParentLevels && directory is not null; level++)
            {
                var path = Path.Combine(directory.FullName, ".env");
                if (File.Exists(path))
                {
                    Apply(path);
                    return;
                }

                directory = directory.Parent;
            }
        }
    }

    private static void Apply(string path)
    {
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"').Trim('\'');

            if (string.IsNullOrEmpty(key))
                continue;

            // Real environment variables win over the file.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}
