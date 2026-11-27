using System.ComponentModel.DataAnnotations;
using Keryx.Benchmarks.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Keryx.Benchmarks.Baseline;

// The real-world scenarios' direct-call baseline: what an application without a mediator writes. Each request has a handler class resolved
// from DI (scoped), and the behavior scenario's handler does the behaviors' work inline (log, validate, look up, log), so the ratio column of
// a real-world scenario is the mediator's cost on top of the same work, not the cost of the work.

public sealed record Ping(int Id);
public sealed record TypicalPing(int Id);
public sealed record QueryPing(int Id);

/// <summary>The plain Ping handler.</summary>
public sealed class PingHandler
{
    public ValueTask<Pong> Handle(Ping request)
    {
        Probe.Hit();
        return new(new Pong(request.Id, PongCatalog.PlainName));
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

/// <summary>The behavior scenario's work, written out: the logging and validation behaviors' bodies around the repository handler's.</summary>
public sealed class TypicalPingHandler(ILogger<TypicalPing> logger, IValidator<TypicalPing> validator, IPongRepository repository)
{
    public ValueTask<Pong> Handle(TypicalPing request)
    {
        RequestLog.Handling(logger, nameof(TypicalPing));
        if (validator.Validate(request) is { } error)
        {
            throw new ValidationException(error);
        }

        Probe.Hit();
        var name = repository.FindName(request.Id) ?? throw new KeyNotFoundException($"No pong with id {request.Id}.");
        var response = new Pong(request.Id, name);
        RequestLog.Handled(logger, nameof(TypicalPing), response);
        return new(response);
    }
}

/// <summary>The data-access handler.</summary>
public sealed class QueryPingHandler(SqliteConnection connection)
{
    public async ValueTask<Pong> Handle(QueryPing request, CancellationToken cancellationToken = default)
    {
        Probe.Hit();
        var name = await PongDatabase.FindNameAsync(connection, request.Id, cancellationToken).ConfigureAwait(false);
        return new(request.Id, name);
    }
}

/// <summary>Registers the real-world baseline's handler classes (scoped), its validator (scoped) and the shared real-world services.</summary>
public static class BaselineSetup
{
    public static IServiceCollection AddBaselineBenchmarks(this IServiceCollection services)
    {
        services.AddRealWorldServices();
        services.AddScoped<IValidator<TypicalPing>, TypicalPingValidator>();
        services.AddScoped<PingHandler>();
        services.AddScoped<TypicalPingHandler>();
        services.AddScoped<QueryPingHandler>();
        return services.AddBaselinePublishBenchmarks();
    }
}
