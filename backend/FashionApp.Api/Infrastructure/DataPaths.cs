using Microsoft.Data.Sqlite;

namespace FashionApp.Api.Infrastructure;

/// <summary>
/// Single source of truth for where the instance keeps its data. Both the
/// SQLite database and the normalized images live under one resolved root, so
/// the personal instance and the public demo can never silently share files.
/// </summary>
public sealed class DataPaths
{
    public const string DefaultDataRoot = "../../data";
    public const string DefaultDatabaseFileName = "fashion.db";
    public const string DefaultUploadsDirectoryName = "uploads";

    public DataPaths(
        string root,
        string databasePath,
        string uploadsPath,
        string? connectionString = null,
        string? personalRoot = null)
    {
        Root = Path.GetFullPath(root);
        PersonalRoot = Path.GetFullPath(personalRoot ?? Root);
        DatabasePath = databasePath == ":memory:"
            ? databasePath
            : Path.GetFullPath(databasePath);
        UploadsPath = Path.GetFullPath(uploadsPath);
        ConnectionString = string.IsNullOrWhiteSpace(connectionString)
            ? new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString()
            : connectionString;
    }

    /// <summary>Resolved root of the active instance (personal or demo).</summary>
    public string Root { get; }

    /// <summary>Resolved personal data root, even when the demo is active.</summary>
    public string PersonalRoot { get; }

    public string PersonalDatabasePath =>
        Path.Combine(PersonalRoot, DefaultDatabaseFileName);

    public string PersonalUploadsPath =>
        Path.Combine(PersonalRoot, DefaultUploadsDirectoryName);

    public string DatabasePath { get; }

    public string UploadsPath { get; }

    public string ConnectionString { get; }

    /// <summary>
    /// Kept as a property because <c>Data Root</c> is the documented setting;
    /// the SQLite connection string and the uploads directory are derived from
    /// it unless an explicit test/CI override is present.
    /// </summary>
    public static DataPaths Resolve(IConfiguration configuration, string contentRootPath)
    {
        var demoEnabled = configuration.GetValue("Demo:Enabled", false);

        var personalRoot = ResolveDirectory(
            configuration["Data:Root"],
            DefaultDataRoot,
            contentRootPath);

        var root = demoEnabled
            ? ResolveDirectory(
                configuration["Demo:DataRoot"],
                Path.Combine(personalRoot, "demo"),
                contentRootPath)
            : personalRoot;

        var (databasePath, connectionString) = ResolveDatabase(
            configuration.GetConnectionString("Default"),
            root,
            contentRootPath);

        var uploadsPath = string.IsNullOrWhiteSpace(configuration["Storage:Root"])
            ? Path.Combine(root, DefaultUploadsDirectoryName)
            : ResolveDirectory(configuration["Storage:Root"], DefaultUploadsDirectoryName, contentRootPath);

        var paths = new DataPaths(root, databasePath, uploadsPath, connectionString, personalRoot);

        if (demoEnabled)
        {
            // The demo must never read (or seed) the personal database/uploads.
            if (SamePath(paths.DatabasePath, paths.PersonalDatabasePath))
            {
                throw new InvalidOperationException(
                    "La demo no puede usar la base de datos personal. " +
                    "Configurá Demo:DataRoot en un directorio propio (por defecto data/demo) " +
                    "o quitá el override de ConnectionStrings:Default.");
            }

            if (SamePath(paths.UploadsPath, paths.PersonalUploadsPath))
            {
                throw new InvalidOperationException(
                    "La demo no puede usar la carpeta de imágenes personal. " +
                    "Configurá Demo:DataRoot en un directorio propio (por defecto data/demo) " +
                    "o quitá el override de Storage:Root.");
            }
        }

        return paths;
    }

    public void EnsureDirectories()
    {
        if (DatabasePath != ":memory:")
        {
            var directory = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
        }

        Directory.CreateDirectory(UploadsPath);
    }

    private static (string DatabasePath, string ConnectionString) ResolveDatabase(
        string? configuredConnectionString,
        string root,
        string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            var databasePath = Path.Combine(root, DefaultDatabaseFileName);
            return (databasePath, $"Data Source={databasePath}");
        }

        var builder = new SqliteConnectionStringBuilder(configuredConnectionString);
        if (string.Equals(builder.DataSource, ":memory:", StringComparison.Ordinal))
            return (":memory:", configuredConnectionString);

        if (!string.IsNullOrWhiteSpace(builder.DataSource))
        {
            builder.DataSource = ResolvePath(builder.DataSource, contentRootPath);
        }

        return (builder.DataSource, builder.ToString());
    }

    private static string ResolveDirectory(
        string? configured,
        string fallback,
        string contentRootPath) =>
        ResolvePath(string.IsNullOrWhiteSpace(configured) ? fallback : configured, contentRootPath);

    private static string ResolvePath(string path, string contentRootPath) =>
        Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(contentRootPath, path));

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
}
