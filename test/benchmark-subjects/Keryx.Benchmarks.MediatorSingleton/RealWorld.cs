using Keryx.Benchmarks.Common;
using Mediator;
using Microsoft.Data.Sqlite;

namespace Keryx.Benchmarks.MediatorSingleton;

// The real-world scenarios (ADR 0001, Benchmarks), with the same bodies as every subject. No TypicalPing: with the singleton lifetime,
// Mediator registers handlers and behaviors as singletons, which cannot take the scoped repository and validator the behavior scenario
// injects (ValidateOnBuild: "Cannot consume scoped service 'Keryx.Benchmarks.Common.IPongRepository' from singleton ..."), so this
// subject is n/a there (open-questions OQ-72).

public sealed record Ping(int Id) : IRequest<Pong>;
public sealed record QueryPing(int Id) : IRequest<Pong>;

public sealed class PingHandler : IRequestHandler<Ping, Pong>
{
    public ValueTask<Pong> Handle(Ping request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return new(new Pong(request.Id, PongCatalog.PlainName));
    }
}

public sealed class QueryPingHandler(SqliteConnection connection) : IRequestHandler<QueryPing, Pong>
{
    public async ValueTask<Pong> Handle(QueryPing request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        var name = await PongDatabase.FindNameAsync(connection, request.Id, cancellationToken).ConfigureAwait(false);
        return new(request.Id, name);
    }
}
