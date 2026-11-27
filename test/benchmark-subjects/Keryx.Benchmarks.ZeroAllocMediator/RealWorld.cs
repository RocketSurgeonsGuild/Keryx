using System.ComponentModel.DataAnnotations;
using Keryx.Benchmarks.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZeroAlloc.Mediator;

namespace Keryx.Benchmarks.ZeroAllocMediator;

// The real-world scenarios (ADR 0001, Benchmarks), with the same bodies as every subject. ZeroAlloc.Mediator behaviors are static and get
// services only through their state: the generated IMediator passes the provider it was resolved from, while the generated static
// Mediator.Send passes a literal null. The library's static-path alternative, an IPipelineBehaviorStateInitializer, hands a process-wide
// bridge the container, which opens a scope per request for scoped services; that would need another behavior body and a scope inside each
// long-lived call, and SetFactory supplies handlers, not behaviors. So the behavior scenario runs through IMediator only (the static path is
// n/a there, open-questions OQ-72), and the data-access handler, which has a constructor dependency, runs through the static path with the
// factory ZeroAllocSetup.UseStaticFactories registers.

public sealed record Ping(int Id) : IRequest<Pong>;
public sealed record TypicalPing(int Id) : IRequest<Pong>;
public sealed record QueryPing(int Id) : IRequest<Pong>;

public sealed class PingHandler : IRequestHandler<Ping, Pong>
{
    public ValueTask<Pong> Handle(Ping request, CancellationToken ct)
    {
        Probe.Hit();
        return new(new Pong(request.Id, PongCatalog.PlainName));
    }
}

public sealed class TypicalPingHandler(IPongRepository repository) : IRequestHandler<TypicalPing, Pong>
{
    public ValueTask<Pong> Handle(TypicalPing request, CancellationToken ct)
    {
        Probe.Hit();
        var name = repository.FindName(request.Id) ?? throw new KeyNotFoundException($"No pong with id {request.Id}.");
        return new(new Pong(request.Id, name));
    }
}

public sealed class QueryPingHandler(SqliteConnection connection) : IRequestHandler<QueryPing, Pong>
{
    public async ValueTask<Pong> Handle(QueryPing request, CancellationToken ct)
    {
        Probe.Hit();
        var name = await PongDatabase.FindNameAsync(connection, request.Id, ct).ConfigureAwait(false);
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

/// <summary>Logs before and after the rest of the pipeline through the request's <see cref="ILogger{TCategoryName}" />, resolved from the state.</summary>
[PipelineBehavior(Order = 100, AppliesTo = typeof(TypicalPing))]
public sealed class TypicalPingLogging : IPipelineBehavior
{
    public static async ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
        where TState : IServiceProvider?
    {
        Probe.Hit();
        var logger = Services(state).GetRequiredService<ILogger<TRequest>>();
        RequestLog.Handling(logger, typeof(TRequest).Name);
        var response = await next(request, state, ct).ConfigureAwait(false);
        RequestLog.Handled(logger, typeof(TRequest).Name, response);
        return response;
    }

    internal static IServiceProvider Services<TState>(TState state) where TState : IServiceProvider? =>
        state ?? throw new InvalidOperationException("The behavior scenario runs through IMediator, which passes its provider as the state.");
}

/// <summary>Runs the request's <see cref="IValidator{T}" />, resolved from the state, and throws when it reports an error.</summary>
[PipelineBehavior(Order = 101, AppliesTo = typeof(TypicalPing))]
public sealed class TypicalPingValidation : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
        where TState : IServiceProvider?
    {
        Probe.Hit();
        if (TypicalPingLogging.Services(state).GetRequiredService<IValidator<TRequest>>().Validate(request) is { } error)
        {
            throw new ValidationException(error);
        }

        return next(request, state, ct);
    }
}
