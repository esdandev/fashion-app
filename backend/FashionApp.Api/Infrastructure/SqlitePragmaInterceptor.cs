using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FashionApp.Api.Infrastructure;

/// <summary>
/// Applies the SQLite pragmas required by the design to every physical connection:
/// WAL journal, a 5 s busy timeout and foreign key enforcement.
/// `journal_mode` is persisted in the database file; `busy_timeout` and `foreign_keys`
/// are per connection, so they must be set every time a connection is opened.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public static readonly SqlitePragmaInterceptor Instance = new();

    private const string Pragmas = """
        PRAGMA journal_mode=WAL;
        PRAGMA busy_timeout=5000;
        PRAGMA foreign_keys=ON;
        """;

    private SqlitePragmaInterceptor()
    {
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }
}
