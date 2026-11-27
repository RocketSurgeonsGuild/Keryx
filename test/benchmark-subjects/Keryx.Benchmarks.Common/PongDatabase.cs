using Microsoft.Data.Sqlite;

namespace Keryx.Benchmarks.Common;

/// <summary>
///     The real-world data-access scenario's database: a named, shared-cache in-memory SQLite database, created and seeded once per process
///     and kept alive by one connection that is never closed. Each container gets its own open connection to it (a singleton
///     <see cref="SqliteConnection" />, see <see cref="RealWorldServices" />), so the measured call is the mediator plus a query and never
///     opens a connection. SQLite's async methods complete synchronously.
/// </summary>
public static class PongDatabase
{
    private const string ConnectionString = "Data Source=keryx-benchmarks-pongs;Mode=Memory;Cache=Shared";
    private static readonly Lazy<SqliteConnection> KeepAlive = new(Create);

    /// <summary>Opens a new connection to the seeded database.</summary>
    public static SqliteConnection Open()
    {
        _ = KeepAlive.Value;
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    /// <summary>The query every subject's data-access handler runs: one parameterized scalar query. Each query is a <see cref="Probe" /> hit.</summary>
    public static async ValueTask<string> FindNameAsync(SqliteConnection connection, int id, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pongs WHERE id = $id";
        _ = command.Parameters.AddWithValue("$id", id);
        var name = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        Probe.Hit();
        return name ?? throw new KeyNotFoundException($"No pong with id {id}.");
    }

    private static SqliteConnection Create()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE IF NOT EXISTS pongs (id INTEGER PRIMARY KEY, name TEXT NOT NULL); DELETE FROM pongs;";
            _ = create.ExecuteNonQuery();
        }

        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO pongs (id, name) VALUES ($id, $name)";
            var idParameter = insert.Parameters.Add("$id", SqliteType.Integer);
            var nameParameter = insert.Parameters.Add("$name", SqliteType.Text);
            for (var id = 1; id <= PongCatalog.Count; id++)
            {
                idParameter.Value = id;
                nameParameter.Value = PongCatalog.Name(id);
                _ = insert.ExecuteNonQuery();
            }
        }

        transaction.Commit();
        return connection;
    }
}
