using Keryx.Benchmarks.Common;
using ZeroAlloc.Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Keryx.Benchmarks.ZeroAllocMediator;

// The real-world notification scenarios (owner decision 30d), with the same bodies as every subject (PingedWork). TypicalPinged's
// handlers look the pong up in the scoped repository and log it (published with a scope per request, through IMediator only: the static
// Mediator class has no scope); QueryPinged's handlers await the SQLite query and log (published sequentially), and ParallelQueryPinged
// has the same handlers under [ParallelNotification]. The static path gets these handlers, which have constructor dependencies, from the
// factories ZeroAllocSetup.UseStaticFactories registers. No notification logging behavior: ZeroAlloc.Mediator's pipeline behaviors wrap
// requests only (open-questions OQ-72).

public sealed record TypicalPinged1(int Id) : INotification;

public sealed record TypicalPinged5(int Id) : INotification;

public sealed record QueryPinged1(int Id) : INotification;

public sealed record QueryPinged5(int Id) : INotification;

[ParallelNotification]
public sealed record ParallelQueryPinged1(int Id) : INotification;

[ParallelNotification]
public sealed record ParallelQueryPinged5(int Id) : INotification;

public sealed class TypicalPinged1Handler(IPongRepository repository, ILogger<TypicalPinged1> logger) : INotificationHandler<TypicalPinged1>
{
    public ValueTask Handle(TypicalPinged1 notification, CancellationToken ct) =>
        PingedWork.Lookup(notification.Id, repository, logger);
}

public sealed class TypicalPinged5HandlerA(IPongRepository repository, ILogger<TypicalPinged5> logger) : INotificationHandler<TypicalPinged5>
{
    public ValueTask Handle(TypicalPinged5 notification, CancellationToken ct) =>
        PingedWork.Lookup(notification.Id, repository, logger);
}

public sealed class TypicalPinged5HandlerB(IPongRepository repository, ILogger<TypicalPinged5> logger) : INotificationHandler<TypicalPinged5>
{
    public ValueTask Handle(TypicalPinged5 notification, CancellationToken ct) =>
        PingedWork.Lookup(notification.Id, repository, logger);
}

public sealed class TypicalPinged5HandlerC(IPongRepository repository, ILogger<TypicalPinged5> logger) : INotificationHandler<TypicalPinged5>
{
    public ValueTask Handle(TypicalPinged5 notification, CancellationToken ct) =>
        PingedWork.Lookup(notification.Id, repository, logger);
}

public sealed class TypicalPinged5HandlerD(IPongRepository repository, ILogger<TypicalPinged5> logger) : INotificationHandler<TypicalPinged5>
{
    public ValueTask Handle(TypicalPinged5 notification, CancellationToken ct) =>
        PingedWork.Lookup(notification.Id, repository, logger);
}

public sealed class TypicalPinged5HandlerE(IPongRepository repository, ILogger<TypicalPinged5> logger) : INotificationHandler<TypicalPinged5>
{
    public ValueTask Handle(TypicalPinged5 notification, CancellationToken ct) =>
        PingedWork.Lookup(notification.Id, repository, logger);
}

public sealed class QueryPinged1Handler(SqliteConnection connection, ILogger<QueryPinged1> logger) : INotificationHandler<QueryPinged1>
{
    public ValueTask Handle(QueryPinged1 notification, CancellationToken ct) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, ct);
}

public sealed class QueryPinged5HandlerA(SqliteConnection connection, ILogger<QueryPinged5> logger) : INotificationHandler<QueryPinged5>
{
    public ValueTask Handle(QueryPinged5 notification, CancellationToken ct) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, ct);
}

public sealed class QueryPinged5HandlerB(SqliteConnection connection, ILogger<QueryPinged5> logger) : INotificationHandler<QueryPinged5>
{
    public ValueTask Handle(QueryPinged5 notification, CancellationToken ct) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, ct);
}

public sealed class QueryPinged5HandlerC(SqliteConnection connection, ILogger<QueryPinged5> logger) : INotificationHandler<QueryPinged5>
{
    public ValueTask Handle(QueryPinged5 notification, CancellationToken ct) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, ct);
}

public sealed class QueryPinged5HandlerD(SqliteConnection connection, ILogger<QueryPinged5> logger) : INotificationHandler<QueryPinged5>
{
    public ValueTask Handle(QueryPinged5 notification, CancellationToken ct) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, ct);
}

public sealed class QueryPinged5HandlerE(SqliteConnection connection, ILogger<QueryPinged5> logger) : INotificationHandler<QueryPinged5>
{
    public ValueTask Handle(QueryPinged5 notification, CancellationToken ct) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, ct);
}

public sealed class ParallelQueryPinged1Handler(SqliteConnection connection, ILogger<ParallelQueryPinged1> logger) : INotificationHandler<ParallelQueryPinged1>
{
    public ValueTask Handle(ParallelQueryPinged1 notification, CancellationToken ct) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, ct);
}

public sealed class ParallelQueryPinged5HandlerA(SqliteConnection connection, ILogger<ParallelQueryPinged5> logger) : INotificationHandler<ParallelQueryPinged5>
{
    public ValueTask Handle(ParallelQueryPinged5 notification, CancellationToken ct) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, ct);
}

public sealed class ParallelQueryPinged5HandlerB(SqliteConnection connection, ILogger<ParallelQueryPinged5> logger) : INotificationHandler<ParallelQueryPinged5>
{
    public ValueTask Handle(ParallelQueryPinged5 notification, CancellationToken ct) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, ct);
}

public sealed class ParallelQueryPinged5HandlerC(SqliteConnection connection, ILogger<ParallelQueryPinged5> logger) : INotificationHandler<ParallelQueryPinged5>
{
    public ValueTask Handle(ParallelQueryPinged5 notification, CancellationToken ct) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, ct);
}

public sealed class ParallelQueryPinged5HandlerD(SqliteConnection connection, ILogger<ParallelQueryPinged5> logger) : INotificationHandler<ParallelQueryPinged5>
{
    public ValueTask Handle(ParallelQueryPinged5 notification, CancellationToken ct) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, ct);
}

public sealed class ParallelQueryPinged5HandlerE(SqliteConnection connection, ILogger<ParallelQueryPinged5> logger) : INotificationHandler<ParallelQueryPinged5>
{
    public ValueTask Handle(ParallelQueryPinged5 notification, CancellationToken ct) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, ct);
}
