using System.ComponentModel.DataAnnotations;
using Immediate.Handlers.Shared;
using Keryx.Benchmarks.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Keryx.Benchmarks.ImmediateHandlers;

// The real-world scenarios (ADR 0001, Benchmarks), with the same bodies as every subject. The logging and validation behaviors are attached
// to the behavior scenario's handler class ([Behaviors]), outermost first; extra HandleAsync parameters are injected through the generated
// Handler class.

public sealed record Ping(int Id);
public sealed record TypicalPing(int Id);
public sealed record QueryPing(int Id);

[Handler]
public static partial class PingHandler
{
    private static ValueTask<Pong> HandleAsync(Ping request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return new(new Pong(request.Id, PongCatalog.PlainName));
    }
}

[Handler]
[Behaviors(typeof(LoggingBehavior<,>), typeof(ValidationBehavior<,>))]
public static partial class TypicalPingHandler
{
    private static ValueTask<Pong> HandleAsync(TypicalPing request, IPongRepository repository, CancellationToken cancellationToken)
    {
        Probe.Hit();
        var name = repository.FindName(request.Id) ?? throw new KeyNotFoundException($"No pong with id {request.Id}.");
        return new(new Pong(request.Id, name));
    }
}

[Handler]
public static partial class QueryPingHandler
{
    private static async ValueTask<Pong> HandleAsync(QueryPing request, SqliteConnection connection, CancellationToken cancellationToken)
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
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<TRequest> logger) : Behavior<TRequest, TResponse>
{
    public override async ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        RequestLog.Handling(logger, MessageName<TRequest>.Value);
        var response = await Next(request, cancellationToken).ConfigureAwait(false);
        RequestLog.Handled(logger, MessageName<TRequest>.Value, response);
        return response;
    }
}

/// <summary>Runs the request's <see cref="IValidator{T}" /> and throws when it reports an error.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IValidator<TRequest> validator) : Behavior<TRequest, TResponse>
{
    public override ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        if (validator.Validate(request) is { } error)
        {
            throw new ValidationException(error);
        }

        return Next(request, cancellationToken);
    }
}
