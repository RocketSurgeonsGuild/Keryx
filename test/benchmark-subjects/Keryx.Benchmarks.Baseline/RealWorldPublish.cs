using Keryx.Benchmarks.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Keryx.Benchmarks.Baseline;

// The real-world notification scenarios' direct-call baseline (owner decision 30d): what an application without a mediator writes. Each
// handler is a class resolved from DI (scoped) running PingedWork's body, and a publisher class (scoped, resolved once per publish) takes the
// handlers in its constructor and calls them in turn; the logged publisher also writes the notification logging behavior's two messages
// inline, and PublishParallel starts every handler before awaiting any, with no failure aggregation (the floor of a parallel strategy).

public sealed record TypicalPinged1(int Id);
public sealed record TypicalPinged5(int Id);
public sealed record QueryPinged1(int Id);
public sealed record QueryPinged5(int Id);

public sealed class TypicalPinged1Handler(IPongRepository repository, ILogger<TypicalPinged1> logger)
{
    public ValueTask Handle(TypicalPinged1 notification) => PingedWork.Lookup(notification.Id, repository, logger);
}

public sealed class TypicalPinged5HandlerA(IPongRepository repository, ILogger<TypicalPinged5> logger)
{
    public ValueTask Handle(TypicalPinged5 notification) => PingedWork.Lookup(notification.Id, repository, logger);
}

public sealed class TypicalPinged5HandlerB(IPongRepository repository, ILogger<TypicalPinged5> logger)
{
    public ValueTask Handle(TypicalPinged5 notification) => PingedWork.Lookup(notification.Id, repository, logger);
}

public sealed class TypicalPinged5HandlerC(IPongRepository repository, ILogger<TypicalPinged5> logger)
{
    public ValueTask Handle(TypicalPinged5 notification) => PingedWork.Lookup(notification.Id, repository, logger);
}

public sealed class TypicalPinged5HandlerD(IPongRepository repository, ILogger<TypicalPinged5> logger)
{
    public ValueTask Handle(TypicalPinged5 notification) => PingedWork.Lookup(notification.Id, repository, logger);
}

public sealed class TypicalPinged5HandlerE(IPongRepository repository, ILogger<TypicalPinged5> logger)
{
    public ValueTask Handle(TypicalPinged5 notification) => PingedWork.Lookup(notification.Id, repository, logger);
}

/// <summary>Calls the one handler of <see cref="TypicalPinged1" />.</summary>
public sealed class TypicalPinged1Publisher(TypicalPinged1Handler a)
{
    public ValueTask Publish(TypicalPinged1 notification) => a.Handle(notification);
}

/// <summary>Calls the five handlers of <see cref="TypicalPinged5" /> in turn.</summary>
public sealed class TypicalPinged5Publisher(
    TypicalPinged5HandlerA a,
    TypicalPinged5HandlerB b,
    TypicalPinged5HandlerC c,
    TypicalPinged5HandlerD d,
    TypicalPinged5HandlerE e
)
{
    public async ValueTask Publish(TypicalPinged5 notification)
    {
        await a.Handle(notification).ConfigureAwait(false);
        await b.Handle(notification).ConfigureAwait(false);
        await c.Handle(notification).ConfigureAwait(false);
        await d.Handle(notification).ConfigureAwait(false);
        await e.Handle(notification).ConfigureAwait(false);
    }
}

/// <summary><see cref="TypicalPinged1Publisher" /> with the notification logging behavior's two messages written inline.</summary>
public sealed class LoggedPinged1Publisher(TypicalPinged1Handler a, ILogger<TypicalPinged1> logger)
{
    public async ValueTask Publish(TypicalPinged1 notification)
    {
        RequestLog.Publishing(logger, MessageName<TypicalPinged1>.Value);
        await a.Handle(notification).ConfigureAwait(false);
        RequestLog.Published(logger, MessageName<TypicalPinged1>.Value);
    }
}

/// <summary><see cref="TypicalPinged5Publisher" /> with the notification logging behavior's two messages written inline.</summary>
public sealed class LoggedPinged5Publisher(
    TypicalPinged5HandlerA a,
    TypicalPinged5HandlerB b,
    TypicalPinged5HandlerC c,
    TypicalPinged5HandlerD d,
    TypicalPinged5HandlerE e,
    ILogger<TypicalPinged5> logger
)
{
    public async ValueTask Publish(TypicalPinged5 notification)
    {
        RequestLog.Publishing(logger, MessageName<TypicalPinged5>.Value);
        await a.Handle(notification).ConfigureAwait(false);
        await b.Handle(notification).ConfigureAwait(false);
        await c.Handle(notification).ConfigureAwait(false);
        await d.Handle(notification).ConfigureAwait(false);
        await e.Handle(notification).ConfigureAwait(false);
        RequestLog.Published(logger, MessageName<TypicalPinged5>.Value);
    }
}

public sealed class QueryPinged1Handler(SqliteConnection connection, ILogger<QueryPinged1> logger)
{
    public ValueTask Handle(QueryPinged1 notification, CancellationToken cancellationToken = default) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public sealed class QueryPinged5HandlerA(SqliteConnection connection, ILogger<QueryPinged5> logger)
{
    public ValueTask Handle(QueryPinged5 notification, CancellationToken cancellationToken = default) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public sealed class QueryPinged5HandlerB(SqliteConnection connection, ILogger<QueryPinged5> logger)
{
    public ValueTask Handle(QueryPinged5 notification, CancellationToken cancellationToken = default) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public sealed class QueryPinged5HandlerC(SqliteConnection connection, ILogger<QueryPinged5> logger)
{
    public ValueTask Handle(QueryPinged5 notification, CancellationToken cancellationToken = default) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public sealed class QueryPinged5HandlerD(SqliteConnection connection, ILogger<QueryPinged5> logger)
{
    public ValueTask Handle(QueryPinged5 notification, CancellationToken cancellationToken = default) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

public sealed class QueryPinged5HandlerE(SqliteConnection connection, ILogger<QueryPinged5> logger)
{
    public ValueTask Handle(QueryPinged5 notification, CancellationToken cancellationToken = default) =>
        PingedWork.QueryAsync(notification.Id, connection, logger, cancellationToken);
}

/// <summary>Calls the one handler of <see cref="QueryPinged1" />, the same for the sequential and the parallel scenario.</summary>
public sealed class QueryPinged1Publisher(QueryPinged1Handler a)
{
    public ValueTask Publish(QueryPinged1 notification) => a.Handle(notification);
}

/// <summary>Awaits the five handlers of <see cref="QueryPinged5" /> in turn, or starts all five before awaiting any.</summary>
public sealed class QueryPinged5Publisher(QueryPinged5HandlerA a, QueryPinged5HandlerB b, QueryPinged5HandlerC c, QueryPinged5HandlerD d, QueryPinged5HandlerE e)
{
    public async ValueTask Publish(QueryPinged5 notification)
    {
        await a.Handle(notification).ConfigureAwait(false);
        await b.Handle(notification).ConfigureAwait(false);
        await c.Handle(notification).ConfigureAwait(false);
        await d.Handle(notification).ConfigureAwait(false);
        await e.Handle(notification).ConfigureAwait(false);
    }

    public async ValueTask PublishParallel(QueryPinged5 notification)
    {
        var first = a.Handle(notification);
        var second = b.Handle(notification);
        var third = c.Handle(notification);
        var fourth = d.Handle(notification);
        var fifth = e.Handle(notification);
        await first.ConfigureAwait(false);
        await second.ConfigureAwait(false);
        await third.ConfigureAwait(false);
        await fourth.ConfigureAwait(false);
        await fifth.ConfigureAwait(false);
    }
}

/// <summary>Registers the real-world notification baseline's handlers and publishers, all scoped.</summary>
public static class BaselinePublishSetup
{
    public static IServiceCollection AddBaselinePublishBenchmarks(this IServiceCollection services)
    {
        services.AddScoped<TypicalPinged1Handler>();
        services.AddScoped<TypicalPinged5HandlerA>();
        services.AddScoped<TypicalPinged5HandlerB>();
        services.AddScoped<TypicalPinged5HandlerC>();
        services.AddScoped<TypicalPinged5HandlerD>();
        services.AddScoped<TypicalPinged5HandlerE>();
        services.AddScoped<TypicalPinged1Publisher>();
        services.AddScoped<TypicalPinged5Publisher>();
        services.AddScoped<LoggedPinged1Publisher>();
        services.AddScoped<LoggedPinged5Publisher>();
        services.AddScoped<QueryPinged1Handler>();
        services.AddScoped<QueryPinged5HandlerA>();
        services.AddScoped<QueryPinged5HandlerB>();
        services.AddScoped<QueryPinged5HandlerC>();
        services.AddScoped<QueryPinged5HandlerD>();
        services.AddScoped<QueryPinged5HandlerE>();
        services.AddScoped<QueryPinged1Publisher>();
        services.AddScoped<QueryPinged5Publisher>();
        return services;
    }
}
