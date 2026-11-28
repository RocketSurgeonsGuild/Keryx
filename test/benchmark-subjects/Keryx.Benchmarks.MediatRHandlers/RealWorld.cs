using System.ComponentModel.DataAnnotations;
using Keryx.Benchmarks.Common;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Keryx.Benchmarks.MediatRHandlers;

// The real-world scenarios (ADR 0001, Benchmarks), with the same bodies as every subject. The logging and validation behaviors are
// constrained open behaviors selected by the ILogged and IValidated markers, registered before the pass-through behaviors.

public sealed record Ping(int Id) : IRequest<Pong>;
public sealed record TypicalPing(int Id) : IRequest<Pong>, ILogged, IValidated;
public sealed record QueryPing(int Id) : IRequest<Pong>;

public interface ILogged;
public interface IValidated;

public sealed class PingHandler : IRequestHandler<Ping, Pong>
{
    public Task<Pong> Handle(Ping request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Task.FromResult(new Pong(request.Id, PongCatalog.PlainName));
    }
}

public sealed class TypicalPingHandler(IPongRepository repository) : IRequestHandler<TypicalPing, Pong>
{
    public Task<Pong> Handle(TypicalPing request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        var name = repository.FindName(request.Id) ?? throw new KeyNotFoundException($"No pong with id {request.Id}.");
        return Task.FromResult(new Pong(request.Id, name));
    }
}

public sealed class QueryPingHandler(SqliteConnection connection) : IRequestHandler<QueryPing, Pong>
{
    public async Task<Pong> Handle(QueryPing request, CancellationToken cancellationToken)
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

/// <summary>Logs before and after the rest of the pipeline through the request's <see cref="ILogger{TCategoryName}" />.</summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<TRequest> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, ILogged
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        Probe.Hit();
        RequestLog.Handling(logger, MessageName<TRequest>.Value);
        var response = await next(cancellationToken).ConfigureAwait(false);
        RequestLog.Handled(logger, MessageName<TRequest>.Value, response);
        return response;
    }
}

/// <summary>Runs the request's <see cref="IValidator{T}" /> and throws when it reports an error.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IValidator<TRequest> validator) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, IValidated
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        Probe.Hit();
        if (validator.Validate(request) is { } error)
        {
            throw new ValidationException(error);
        }

        return next(cancellationToken);
    }
}
