using Keryx.Benchmarks.Common;
using Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Keryx.Benchmarks.MediatorSingleton;

// The real-world async-handler notification scenario (owner decision 30d), with the same bodies as every subject (PingedWork): the
// handlers await the SQLite query and log (published sequentially, Mediator's default ForeachAwaitPublisher). No TypicalPinged: its
// handlers take the scoped repository, which this subject's singleton handlers cannot (open-questions OQ-72).

public sealed record QueryPinged1(int Id) : INotification;

public sealed record QueryPinged5(int Id) : INotification;

public sealed class QueryPinged1Handler(SqliteConnection connection, ILogger<QueryPinged1> logger) : INotificationHandler<QueryPinged1>
{
    public ValueTask Handle(QueryPinged1 notification, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public sealed class QueryPinged5HandlerA(SqliteConnection connection, ILogger<QueryPinged5> logger) : INotificationHandler<QueryPinged5>
{
    public ValueTask Handle(QueryPinged5 notification, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public sealed class QueryPinged5HandlerB(SqliteConnection connection, ILogger<QueryPinged5> logger) : INotificationHandler<QueryPinged5>
{
    public ValueTask Handle(QueryPinged5 notification, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public sealed class QueryPinged5HandlerC(SqliteConnection connection, ILogger<QueryPinged5> logger) : INotificationHandler<QueryPinged5>
{
    public ValueTask Handle(QueryPinged5 notification, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public sealed class QueryPinged5HandlerD(SqliteConnection connection, ILogger<QueryPinged5> logger) : INotificationHandler<QueryPinged5>
{
    public ValueTask Handle(QueryPinged5 notification, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public sealed class QueryPinged5HandlerE(SqliteConnection connection, ILogger<QueryPinged5> logger) : INotificationHandler<QueryPinged5>
{
    public ValueTask Handle(QueryPinged5 notification, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}
