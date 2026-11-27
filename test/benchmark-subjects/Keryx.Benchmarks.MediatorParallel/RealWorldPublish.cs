using Keryx.Benchmarks.Common;
using Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Keryx.Benchmarks.MediatorParallel;

// The real-world parallel publish scenario (owner decision 30d): the same QueryPinged handlers as the other Mediator subjects (the bodies
// are PingedWork's), published by Mediator's TaskWhenAllPublisher, which MediatorParallelSetup chooses.

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
