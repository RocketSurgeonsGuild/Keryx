using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Keryx.Benchmarks.Common;

/// <summary>
///     The body of every real-world notification handler (owner decision 30d), called by each subject's handler classes so every library runs
///     the same work: <see cref="Lookup" /> for the scope-per-request publish, <see cref="QueryAsync" /> for the async-handler publish
///     (sequential and parallel). Each body is a handler <see cref="Probe" /> hit, plus the lookup's or the query's and the log message's.
/// </summary>
public static class PingedWork
{
    /// <summary>Looks the pong up in the scoped repository and logs it. Completes synchronously.</summary>
    public static ValueTask Lookup(int id, IPongRepository repository, ILogger logger)
    {
        Probe.Hit();
        var name = repository.FindName(id) ?? throw new KeyNotFoundException($"No pong with id {id}.");
        RequestLog.Pinged(logger, id, name);
        return default;
    }

    /// <summary>Awaits the SQLite query for the pong's name (which completes synchronously) and logs it.</summary>
    public static async ValueTask QueryAsync(int id, SqliteConnection connection, ILogger logger, CancellationToken cancellationToken)
    {
        Probe.Hit();
        var name = await PongDatabase.FindNameAsync(connection, id, cancellationToken).ConfigureAwait(false);
        RequestLog.Pinged(logger, id, name);
    }
}
