using FashionApp.Api.Infrastructure;

namespace FashionApp.Tests;

public sealed class EnvFileTests
{
    [Fact]
    public void Loads_values_and_does_not_overwrite_existing_environment()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "fashion-env-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var key = $"FASHIONAPP_TEST_{Guid.NewGuid():N}";

        try
        {
            File.WriteAllText(
                Path.Combine(directory, ".env"),
                $"""
                # comentario
                {key}=desde-el-archivo
                """);

            EnvFile.Load(directory);
            Assert.Equal("desde-el-archivo", Environment.GetEnvironmentVariable(key));

            Environment.SetEnvironmentVariable(key, "desde-el-entorno");
            EnvFile.Load(directory);
            Assert.Equal("desde-el-entorno", Environment.GetEnvironmentVariable(key));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
            Directory.Delete(directory, recursive: true);
        }
    }
}
