using Keryx.Benchmarks.Common;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Keryx.Benchmarks.MediatRHandlers;

// The real-world notification scenarios (owner decision 30d), with the same bodies as every subject (PingedWork). TypicalPinged's
// handlers look the pong up in the scoped repository and log it (published with a scope per request); QueryPinged's handlers await the
// SQLite query and log, published sequentially by the default container (ForeachAwaitPublisher) and in parallel by the container
// MediatRSetup.AddMediatRParallelBenchmarks builds (TaskWhenAllPublisher). No notification logging behavior: MediatR's pipeline behaviors
// wrap Send only, and Publish hands the handlers to its INotificationPublisher directly (open-questions OQ-72).

public sealed record TypicalPinged1(int Id) : INotification;

public sealed record TypicalPinged5(int Id) : INotification;

public sealed record QueryPinged1(int Id) : INotification;

public sealed record QueryPinged5(int Id) : INotification;

public sealed class TypicalPinged1Handler(IPongRepository repository, ILogger<TypicalPinged1> logger) : INotificationHandler<TypicalPinged1>
{
    public Task Handle(TypicalPinged1 notification, CancellationToken cancellationToken) =>
        PingedWork.Lookup(notification.Id, repository, logger).AsTask();
}

public sealed class TypicalPinged5HandlerA(IPongRepository repository, ILogger<TypicalPinged5> logger) : INotificationHandler<TypicalPinged5>
{
    public Task Handle(TypicalPinged5 notification, CancellationToken cancellationToken) =>
        PingedWork.Lookup(notification.Id, repository, logger).AsTask();
}

public sealed class TypicalPinged5HandlerB(IPongRepository repository, ILogger<TypicalPinged5> logger) : INotificationHandler<TypicalPinged5>
{
    public Task Handle(TypicalPinged5 notification, CancellationToken cancellationToken) =>
        PingedWork.Lookup(notification.Id, repository, logger).AsTask();
}

public sealed class TypicalPinged5HandlerC(IPongRepository repository, ILogger<TypicalPinged5> logger) : INotificationHandler<TypicalPinged5>
{
    public Task Handle(TypicalPinged5 notification, CancellationToken cancellationToken) =>
        PingedWork.Lookup(notification.Id, repository, logger).AsTask();
}

public sealed class TypicalPinged5HandlerD(IPongRepository repository, ILogger<TypicalPinged5> logger) : INotificationHandler<TypicalPinged5>
{
    public Task Handle(TypicalPinged5 notification, CancellationToken cancellationToken) =>
        PingedWork.Lookup(notification.Id, repository, logger).AsTask();
}

public sealed class TypicalPinged5HandlerE(IPongRepository repository, ILogger<TypicalPinged5> logger) : INotificationHandler<TypicalPinged5>
{
    public Task Handle(TypicalPinged5 notification, CancellationToken cancellationToken) =>
        PingedWork.Lookup(notification.Id, repository, logger).AsTask();
}

public sealed class QueryPinged1Handler(SqliteConnection connection, ILogger<QueryPinged1> logger) : INotificationHandler<QueryPinged1>
{
    public Task Handle(QueryPinged1 notification, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken).AsTask();
}

public sealed class QueryPinged5HandlerA(SqliteConnection connection, ILogger<QueryPinged5> logger) : INotificationHandler<QueryPinged5>
{
    public Task Handle(QueryPinged5 notification, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken).AsTask();
}

public sealed class QueryPinged5HandlerB(SqliteConnection connection, ILogger<QueryPinged5> logger) : INotificationHandler<QueryPinged5>
{
    public Task Handle(QueryPinged5 notification, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken).AsTask();
}

public sealed class QueryPinged5HandlerC(SqliteConnection connection, ILogger<QueryPinged5> logger) : INotificationHandler<QueryPinged5>
{
    public Task Handle(QueryPinged5 notification, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken).AsTask();
}

public sealed class QueryPinged5HandlerD(SqliteConnection connection, ILogger<QueryPinged5> logger) : INotificationHandler<QueryPinged5>
{
    public Task Handle(QueryPinged5 notification, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken).AsTask();
}

public sealed class QueryPinged5HandlerE(SqliteConnection connection, ILogger<QueryPinged5> logger) : INotificationHandler<QueryPinged5>
{
    public Task Handle(QueryPinged5 notification, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken).AsTask();
}
