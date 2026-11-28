using Keryx.Benchmarks.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Keryx.Benchmarks.KeryxHost;

// The real-world notification scenarios (owner decision 30d), with the same bodies as every subject (PingedWork). TypicalPinged's
// handlers look the pong up in the scoped repository and log it (published with a scope per request); LoggedPinged has the same handlers
// and the notification logging behavior (LoggingBehavior's [NotificationBehavior] method, attached with [Behavior] on the type);
// QueryPinged's handlers await the SQLite query and log (published sequentially), and ParallelQueryPinged has the same handlers under
// [PublishStrategy(PublishStrategy.Parallel)].

public sealed record TypicalPinged1(int Id) : INotification;
public sealed record TypicalPinged5(int Id) : INotification;

[Behavior(typeof(LoggingBehavior))]
public sealed record LoggedPinged1(int Id) : INotification;

[Behavior(typeof(LoggingBehavior))]
public sealed record LoggedPinged5(int Id) : INotification;

public sealed record QueryPinged1(int Id) : INotification;
public sealed record QueryPinged5(int Id) : INotification;

[PublishStrategy(PublishStrategy.Parallel)]
public sealed record ParallelQueryPinged1(int Id) : INotification;

[PublishStrategy(PublishStrategy.Parallel)]
public sealed record ParallelQueryPinged5(int Id) : INotification;

public partial class TypicalPinged1Handler
{
    private static ValueTask Handle(TypicalPinged1 notification, IPongRepository repository, ILogger<TypicalPinged1> logger) =>
        PingedWork.Lookup(notification.Id, repository, logger);
}

public partial class TypicalPinged5HandlerA
{
    private static ValueTask Handle(TypicalPinged5 notification, IPongRepository repository, ILogger<TypicalPinged5> logger) =>
        PingedWork.Lookup(notification.Id, repository, logger);
}

public partial class TypicalPinged5HandlerB
{
    private static ValueTask Handle(TypicalPinged5 notification, IPongRepository repository, ILogger<TypicalPinged5> logger) =>
        PingedWork.Lookup(notification.Id, repository, logger);
}

public partial class TypicalPinged5HandlerC
{
    private static ValueTask Handle(TypicalPinged5 notification, IPongRepository repository, ILogger<TypicalPinged5> logger) =>
        PingedWork.Lookup(notification.Id, repository, logger);
}

public partial class TypicalPinged5HandlerD
{
    private static ValueTask Handle(TypicalPinged5 notification, IPongRepository repository, ILogger<TypicalPinged5> logger) =>
        PingedWork.Lookup(notification.Id, repository, logger);
}

public partial class TypicalPinged5HandlerE
{
    private static ValueTask Handle(TypicalPinged5 notification, IPongRepository repository, ILogger<TypicalPinged5> logger) =>
        PingedWork.Lookup(notification.Id, repository, logger);
}

public partial class LoggedPinged1Handler
{
    private static ValueTask Handle(LoggedPinged1 notification, IPongRepository repository, ILogger<LoggedPinged1> logger) =>
        PingedWork.Lookup(notification.Id, repository, logger);
}

public partial class LoggedPinged5HandlerA
{
    private static ValueTask Handle(LoggedPinged5 notification, IPongRepository repository, ILogger<LoggedPinged5> logger) =>
        PingedWork.Lookup(notification.Id, repository, logger);
}

public partial class LoggedPinged5HandlerB
{
    private static ValueTask Handle(LoggedPinged5 notification, IPongRepository repository, ILogger<LoggedPinged5> logger) =>
        PingedWork.Lookup(notification.Id, repository, logger);
}

public partial class LoggedPinged5HandlerC
{
    private static ValueTask Handle(LoggedPinged5 notification, IPongRepository repository, ILogger<LoggedPinged5> logger) =>
        PingedWork.Lookup(notification.Id, repository, logger);
}

public partial class LoggedPinged5HandlerD
{
    private static ValueTask Handle(LoggedPinged5 notification, IPongRepository repository, ILogger<LoggedPinged5> logger) =>
        PingedWork.Lookup(notification.Id, repository, logger);
}

public partial class LoggedPinged5HandlerE
{
    private static ValueTask Handle(LoggedPinged5 notification, IPongRepository repository, ILogger<LoggedPinged5> logger) =>
        PingedWork.Lookup(notification.Id, repository, logger);
}

public partial class QueryPinged1Handler
{
    private static ValueTask Handle(QueryPinged1 notification, SqliteConnection connection, ILogger<QueryPinged1> logger, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public partial class QueryPinged5HandlerA
{
    private static ValueTask Handle(QueryPinged5 notification, SqliteConnection connection, ILogger<QueryPinged5> logger, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public partial class QueryPinged5HandlerB
{
    private static ValueTask Handle(QueryPinged5 notification, SqliteConnection connection, ILogger<QueryPinged5> logger, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public partial class QueryPinged5HandlerC
{
    private static ValueTask Handle(QueryPinged5 notification, SqliteConnection connection, ILogger<QueryPinged5> logger, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public partial class QueryPinged5HandlerD
{
    private static ValueTask Handle(QueryPinged5 notification, SqliteConnection connection, ILogger<QueryPinged5> logger, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public partial class QueryPinged5HandlerE
{
    private static ValueTask Handle(QueryPinged5 notification, SqliteConnection connection, ILogger<QueryPinged5> logger, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public partial class ParallelQueryPinged1Handler
{
    private static ValueTask Handle(ParallelQueryPinged1 notification, SqliteConnection connection, ILogger<ParallelQueryPinged1> logger, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public partial class ParallelQueryPinged5HandlerA
{
    private static ValueTask Handle(ParallelQueryPinged5 notification, SqliteConnection connection, ILogger<ParallelQueryPinged5> logger, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public partial class ParallelQueryPinged5HandlerB
{
    private static ValueTask Handle(ParallelQueryPinged5 notification, SqliteConnection connection, ILogger<ParallelQueryPinged5> logger, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public partial class ParallelQueryPinged5HandlerC
{
    private static ValueTask Handle(ParallelQueryPinged5 notification, SqliteConnection connection, ILogger<ParallelQueryPinged5> logger, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public partial class ParallelQueryPinged5HandlerD
{
    private static ValueTask Handle(ParallelQueryPinged5 notification, SqliteConnection connection, ILogger<ParallelQueryPinged5> logger, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public partial class ParallelQueryPinged5HandlerE
{
    private static ValueTask Handle(ParallelQueryPinged5 notification, SqliteConnection connection, ILogger<ParallelQueryPinged5> logger, CancellationToken cancellationToken) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}
