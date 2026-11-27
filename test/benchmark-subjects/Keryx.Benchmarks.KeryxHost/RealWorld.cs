using System.ComponentModel.DataAnnotations;
using Keryx.Benchmarks.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Keryx.Benchmarks.KeryxHost;

// The real-world scenarios (ADR 0001, Benchmarks): Ping -> Pong with no behaviors, with a logging and a validation behavior around a handler
// that injects a scoped repository, and with a handler that queries SQLite. The bodies are the same in every subject.

public sealed record Ping(int Id) : IRequest<Pong>;
public sealed record TypicalPing(int Id) : IRequest<Pong>;
public sealed record QueryPing(int Id) : IRequest<Pong>;

public static partial class PingHandler
{
    private static ValueTask<Pong> Handle(Ping request)
    {
        Probe.Hit();
        return new(new Pong(request.Id, PongCatalog.PlainName));
    }
}

public partial class TypicalPingHandler
{
    [Behavior(typeof(LoggingBehavior))]
    [Behavior(typeof(ValidationBehavior))]
    private static ValueTask<Pong> Handle(TypicalPing request, IPongRepository repository)
    {
        Probe.Hit();
        var name = repository.FindName(request.Id) ?? throw new KeyNotFoundException($"No pong with id {request.Id}.");
        return new(new Pong(request.Id, name));
    }
}

public partial class QueryPingHandler
{
    private static async ValueTask<Pong> Handle(QueryPing request, SqliteConnection connection, CancellationToken cancellationToken)
    {
        Probe.Hit();
        var name = await PongDatabase.FindNameAsync(connection, request.Id, cancellationToken).ConfigureAwait(false);
        return new(request.Id, name);
    }
}

/// <summary>The hand-written validator, the same rule as every subject's.</summary>
public sealed class TypicalPingValidator : IValidator<TypicalPing>
{
    public string? Validate(TypicalPing instance)
    {
        Probe.Hit();
        return PongCatalog.CheckId(instance.Id);
    }
}

/// <summary>
///     Logs before and after the rest of the pipeline through the message's <see cref="ILogger{TCategoryName}" />: a request's handler, or a
///     notification's handlers (the real-world notification scenarios, RealWorldPublish.cs).
/// </summary>
[RequestBehavior]
[NotificationBehavior]
[BehaviorOrder(Phase.Observability)]
public static class LoggingBehavior
{
    public static async ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(
        TRequest request,
        TNext next,
        ILogger<TRequest> logger,
        CancellationToken cancellationToken
    )
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        Probe.Hit();
        RequestLog.Handling(logger, MessageName<TRequest>.Value);
        var response = await next.InvokeAsync(cancellationToken).ConfigureAwait(false);
        RequestLog.Handled(logger, MessageName<TRequest>.Value, response);
        return response;
    }

    public static async ValueTask Handle<TNotification, TNext>(
        TNotification notification,
        TNext next,
        ILogger<TNotification> logger,
        CancellationToken cancellationToken
    )
        where TNotification : INotification
        where TNext : struct, INext
    {
        Probe.Hit();
        RequestLog.Publishing(logger, MessageName<TNotification>.Value);
        await next.InvokeAsync(cancellationToken).ConfigureAwait(false);
        RequestLog.Published(logger, MessageName<TNotification>.Value);
    }
}

/// <summary>Runs the request's <see cref="IValidator{T}" /> and throws when it reports an error.</summary>
[RequestBehavior]
[BehaviorOrder(Phase.Validation)]
public static class ValidationBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(
        TRequest request,
        TNext next,
        IValidator<TRequest> validator,
        CancellationToken cancellationToken
    )
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        Probe.Hit();
        if (validator.Validate(request) is { } error)
        {
            throw new ValidationException(error);
        }

        return next.InvokeAsync(cancellationToken);
    }
}
