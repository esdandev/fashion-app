using System.Data;
using System.Data.Common;
using System.Globalization;
using FashionApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed class SqlitePragmaTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task Test_created_database_uses_required_pragmas()
    {
        await using var db = TestDb.Create();
        await AssertRequiredPragmasAsync(db);
    }

    [Fact]
    public async Task Api_database_uses_required_pragmas()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await AssertRequiredPragmasAsync(db);
    }

    private static async Task AssertRequiredPragmasAsync(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync();

        try
        {
            Assert.Equal("wal", await ScalarAsync(connection, "PRAGMA journal_mode;"));
            Assert.Equal("5000", await ScalarAsync(connection, "PRAGMA busy_timeout;"));
            Assert.Equal("1", await ScalarAsync(connection, "PRAGMA foreign_keys;"));
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }
    }

    private static async Task<string?> ScalarAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
}
