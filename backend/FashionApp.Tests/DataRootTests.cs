using FashionApp.Api.Infrastructure;
using Microsoft.Extensions.Configuration;

public sealed class DataRootTests
{
    [Fact]
    public void Default_config_resolves_database_and_uploads_under_repo_data()
    {
        var apiDirectory = ApiProjectDirectory();
        var repoData = Path.GetFullPath(Path.Combine(apiDirectory, "..", "..", "data"));

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var paths = DataPaths.Resolve(configuration, apiDirectory);

        Assert.Equal(repoData, paths.Root);
        Assert.Equal(Path.Combine(repoData, "fashion.db"), paths.DatabasePath);
        Assert.Equal(Path.Combine(repoData, "uploads"), paths.UploadsPath);
        Assert.StartsWith($"Data Source={Path.Combine(repoData, "fashion.db")}", paths.ConnectionString);
    }

    [Fact]
    public void Demo_data_root_defaults_to_its_own_directory()
    {
        var apiDirectory = ApiProjectDirectory();
        var repoData = Path.GetFullPath(Path.Combine(apiDirectory, "..", "..", "data"));

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Demo:Enabled"] = "true"
            })
            .Build();

        var paths = DataPaths.Resolve(configuration, apiDirectory);

        Assert.Equal(Path.Combine(repoData, "demo"), paths.Root);
        Assert.Equal(Path.Combine(repoData, "demo", "fashion.db"), paths.DatabasePath);
        Assert.Equal(Path.Combine(repoData, "demo", "uploads"), paths.UploadsPath);
        Assert.Equal(Path.Combine(repoData, "fashion.db"), paths.PersonalDatabasePath);
    }

    [Fact]
    public void Demo_cannot_point_at_the_personal_data_root()
    {
        var apiDirectory = ApiProjectDirectory();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Demo:Enabled"] = "true",
                ["Demo:DataRoot"] = "../../data"
            })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(
            () => DataPaths.Resolve(configuration, apiDirectory));

        Assert.Contains("base de datos personal", error.Message);
    }

    private static string ApiProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "FashionApp.Api", "appsettings.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "FashionApp.Api");
    }
}
