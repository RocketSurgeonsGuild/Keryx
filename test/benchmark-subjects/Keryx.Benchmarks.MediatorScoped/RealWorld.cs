using System.ComponentModel.DataAnnotations;
using Keryx.Benchmarks.Common;
using Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Keryx.Benchmarks.MediatorScoped;

// The real-world scenarios (ADR 0001, Benchmarks), with the same bodies as every subject. The logging and validation behaviors are
// constrained open behaviors selected by the ILogged and IValidated markers, listed first in the setup's PipelineBehaviors.

public sealed record Ping(int Id) : IRequest<Pong>;
public sealed record TypicalPing(int Id) : IRequest<Pong>, ILogged, IValidated;
public sealed record QueryPing(int Id) : IRequest<Pong>;

public interface ILogged;
public interface IValidated;

public sealed class PingHandler : IRequestHandler<Ping, Pong>
{
    public ValueTask<Pong> Handle(Ping request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return new(new Pong(request.Id, PongCatalog.PlainName));
    }
}

public sealed class TypicalPingHandler(IPongRepository repository) : IRequestHandler<TypicalPing, Pong>
{
    public ValueTask<Pong> Handle(TypicalPing request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        var name = repository.FindName(request.Id) ?? throw new KeyNotFoundException($"No pong with id {request.Id}.");
        return new(new Pong(request.Id, name));
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

public sealed class QueryPingHandler(SqliteConnection connection) : IRequestHandler<QueryPing, Pong>
{
    public async ValueTask<Pong> Handle(QueryPing request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        var name = await PongDatabase.FindNameAsync(connection, request.Id, cancellationToken).ConfigureAwait(false);
        return new(request.Id, name);
    }
}

/// <summary>Logs before and after the rest of the pipeline through the request's <see cref="ILogger{TCategoryName}" />.</summary>
public sealed class LoggingBehavior<TMessage, TResponse>(ILogger<TMessage> logger) : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage, ILogged
{
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        Probe.Hit();
        RequestLog.Handling(logger, typeof(TMessage).Name);
        var response = await next(message, cancellationToken).ConfigureAwait(false);
        RequestLog.Handled(logger, typeof(TMessage).Name, response);
        return response;
    }
}

/// <summary>Runs the request's <see cref="IValidator{T}" /> and throws when it reports an error.</summary>
public sealed class ValidationBehavior<TMessage, TResponse>(IValidator<TMessage> validator) : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage, IValidated
{
    public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        Probe.Hit();
        if (validator.Validate(message) is { } error)
        {
            throw new ValidationException(error);
        }

        return next(message, cancellationToken);
    }
}
