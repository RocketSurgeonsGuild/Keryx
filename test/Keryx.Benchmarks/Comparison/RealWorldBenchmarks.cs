// The real-world Ping -> Pong scenarios (ADR 0001, Benchmarks; owner decision 30, 2026-11-27): a plain handler, a logging and a validation
// behavior around a handler that injects a scoped repository, and a handler that queries SQLite. "Long-lived scope" rows send through the
// mediator resolved once from the scope that lives as long as the benchmark, like the other comparison scenarios; "scope per request" rows
// create a scope, resolve the library's entry point from it, send and dispose the scope inside the measured method, the way an ASP.NET Core
// request does. The baseline is a hand-written handler class resolved from DI that does the same work (the behavior scenario's baseline logs
// and validates inline), so the ratio is the mediator's cost on top of the work. A subject a scenario cannot run is left out (n/a, with the
// reason on the class). The notification scenarios are in RealWorldPublishBenchmarks.cs.
using BenchmarkDotNet.Attributes;
using Keryx.Benchmarks.Common;
using Microsoft.Extensions.DependencyInjection;
using B = Keryx.Benchmarks.Baseline;
using IH = Keryx.Benchmarks.ImmediateHandlers;
using K = Keryx.Benchmarks.KeryxHost;
using MR = Keryx.Benchmarks.MediatRHandlers;
using MS = Keryx.Benchmarks.MediatorScoped;
using MSi = Keryx.Benchmarks.MediatorSingleton;
using ZA = Keryx.Benchmarks.ZeroAllocMediator;

namespace Keryx.Benchmarks.Comparison;

/// <summary><c>Send(Ping)</c> to a handler that returns a new <see cref="Pong" />, no behaviors, from the long-lived scope.</summary>
[Expect(Result = PongCatalog.BenchmarkId, Name = PongCatalog.PlainName, Hits = 1)]
public class RealWorldPing : ComparisonBenchmark
{
    private readonly B.Ping _baseline = new(PongCatalog.BenchmarkId);
    private readonly IH.Ping _immediate = new(PongCatalog.BenchmarkId);
    private readonly K.Ping _keryx = new(PongCatalog.BenchmarkId);
    private readonly MR.Ping _mediatr = new(PongCatalog.BenchmarkId);
    private readonly MS.Ping _mediatorScoped = new(PongCatalog.BenchmarkId);
    private readonly MSi.Ping _mediatorSingleton = new(PongCatalog.BenchmarkId);
    private readonly ZA.Ping _zeroAlloc = new(PongCatalog.BenchmarkId);
    private B.PingHandler _baselineHandler = null!;
    private IH.PingHandler.Handler _immediateHandler = null!;

    private protected override void Resolve(Subjects subjects)
    {
        _baselineHandler = subjects.LongLived(Library.Baseline).GetRequiredService<B.PingHandler>();
        _immediateHandler = subjects.Immediate<IH.PingHandler.Handler>();
    }

    [Benchmark(Baseline = true)]
    public ValueTask<Pong> Baseline() => _baselineHandler.Handle(_baseline);

    [Benchmark]
    public ValueTask<Pong> Keryx_Typed() => KeryxTyped.Send(_keryx);

    [Benchmark]
    public ValueTask<Pong> Keryx_Generic() => KeryxGeneric.Send(_keryx);

    [Benchmark]
    public ValueTask<Pong> MediatorScoped_Typed() => MediatorScopedTyped.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<Pong> MediatorScoped_Generic() => MediatorScopedGeneric.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<Pong> MediatorSingleton_Typed() => MediatorSingletonTyped.Send(_mediatorSingleton);

    [Benchmark]
    public ValueTask<Pong> MediatorSingleton_Generic() => MediatorSingletonGeneric.Send(_mediatorSingleton);

    [Benchmark]
    public Task<Pong> MediatR_Generic() => MediatR.Send(_mediatr);

    [Benchmark]
    public ValueTask<Pong> ImmediateHandlers_Typed() => _immediateHandler.HandleAsync(_immediate);

    [Benchmark]
    public ValueTask<Pong> ZeroAlloc_Static() => ZeroAlloc.Mediator.Mediator.Send(_zeroAlloc);

    [Benchmark]
    public ValueTask<Pong> ZeroAlloc_IMediator() => ZeroAllocMediator.Send(_zeroAlloc);
}

/// <summary>
///     <c>Send(Ping)</c> with a scope per request: create a scope, resolve the entry point, send, dispose. ZeroAlloc.Mediator's static
///     <c>Mediator</c> has no scope (n/a).
/// </summary>
[Expect(Result = PongCatalog.BenchmarkId, Name = PongCatalog.PlainName, Hits = 1)]
public class RealWorldPingScopePerRequest : ComparisonBenchmark
{
    private readonly B.Ping _baseline = new(PongCatalog.BenchmarkId);
    private readonly IH.Ping _immediate = new(PongCatalog.BenchmarkId);
    private readonly K.Ping _keryx = new(PongCatalog.BenchmarkId);
    private readonly MR.Ping _mediatr = new(PongCatalog.BenchmarkId);
    private readonly MS.Ping _mediatorScoped = new(PongCatalog.BenchmarkId);
    private readonly MSi.Ping _mediatorSingleton = new(PongCatalog.BenchmarkId);
    private readonly ZA.Ping _zeroAlloc = new(PongCatalog.BenchmarkId);
    private ScopeFactories _scopes = null!;

    private protected override void Resolve(Subjects subjects) => _scopes = new(subjects);

    [Benchmark(Baseline = true)]
    public async ValueTask<Pong> Baseline()
    {
        using var scope = _scopes.Baseline.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<B.PingHandler>().Handle(_baseline);
    }

    [Benchmark]
    public async ValueTask<Pong> Keryx_Typed()
    {
        using var scope = _scopes.Keryx.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<K.KeryxMediator>().Send(_keryx);
    }

    [Benchmark]
    public async ValueTask<Pong> Keryx_Generic()
    {
        using var scope = _scopes.Keryx.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(_keryx);
    }

    [Benchmark]
    public async ValueTask<Pong> MediatorScoped_Typed()
    {
        using var scope = _scopes.MediatorScoped.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MS.Mediator>().Send(_mediatorScoped);
    }

    [Benchmark]
    public async ValueTask<Pong> MediatorScoped_Generic()
    {
        using var scope = _scopes.MediatorScoped.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<Mediator.IMediator>().Send(_mediatorScoped);
    }

    [Benchmark]
    public async ValueTask<Pong> MediatorSingleton_Typed()
    {
        using var scope = _scopes.MediatorSingleton.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MSi.Mediator>().Send(_mediatorSingleton);
    }

    [Benchmark]
    public async ValueTask<Pong> MediatorSingleton_Generic()
    {
        using var scope = _scopes.MediatorSingleton.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<Mediator.IMediator>().Send(_mediatorSingleton);
    }

    [Benchmark]
    public async ValueTask<Pong> MediatR_Generic()
    {
        using var scope = _scopes.MediatR.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MediatR.IMediator>().Send(_mediatr);
    }

    [Benchmark]
    public async ValueTask<Pong> ImmediateHandlers_Typed()
    {
        using var scope = _scopes.ImmediateHandlers.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IH.PingHandler.Handler>().HandleAsync(_immediate);
    }

    [Benchmark]
    public async ValueTask<Pong> ZeroAlloc_IMediator()
    {
        using var scope = _scopes.ZeroAlloc.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ZeroAlloc.Mediator.IMediator>().Send(_zeroAlloc);
    }
}

/// <summary>
///     <c>Send(TypicalPing)</c> through a logging behavior (Microsoft.Extensions.Logging, two formatted messages) and a validation behavior
///     (a hand-written <c>IValidator&lt;TypicalPing&gt;</c>, scoped) to a handler that injects the scoped <see cref="IPongRepository" />, from
///     the long-lived scope. n/a: Mediator singleton (its singleton handlers and behaviors cannot take the scoped repository and validator)
///     and ZeroAlloc.Mediator's static <c>Mediator</c> (its generated static <c>Send</c> passes a literal <see langword="null" /> as the
///     behaviors' state, and the library's static-path state, an <c>IPipelineBehaviorStateInitializer</c>, would need another behavior body
///     that opens a scope per call; open-questions OQ-72).
/// </summary>
[Expect(Result = PongCatalog.BenchmarkId, Name = PongCatalog.BenchmarkName, Hits = 7, BaselineHits = 5)]
public class RealWorldBehaviors : ComparisonBenchmark
{
    private readonly B.TypicalPing _baseline = new(PongCatalog.BenchmarkId);
    private readonly IH.TypicalPing _immediate = new(PongCatalog.BenchmarkId);
    private readonly K.TypicalPing _keryx = new(PongCatalog.BenchmarkId);
    private readonly MR.TypicalPing _mediatr = new(PongCatalog.BenchmarkId);
    private readonly MS.TypicalPing _mediatorScoped = new(PongCatalog.BenchmarkId);
    private readonly ZA.TypicalPing _zeroAlloc = new(PongCatalog.BenchmarkId);
    private B.TypicalPingHandler _baselineHandler = null!;
    private IH.TypicalPingHandler.Handler _immediateHandler = null!;

    private protected override void Resolve(Subjects subjects)
    {
        _baselineHandler = subjects.LongLived(Library.Baseline).GetRequiredService<B.TypicalPingHandler>();
        _immediateHandler = subjects.Immediate<IH.TypicalPingHandler.Handler>();
    }

    [Benchmark(Baseline = true)]
    public ValueTask<Pong> Baseline() => _baselineHandler.Handle(_baseline);

    [Benchmark]
    public ValueTask<Pong> Keryx_Typed() => KeryxTyped.Send(_keryx);

    [Benchmark]
    public ValueTask<Pong> Keryx_Generic() => KeryxGeneric.Send(_keryx);

    [Benchmark]
    public ValueTask<Pong> MediatorScoped_Typed() => MediatorScopedTyped.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<Pong> MediatorScoped_Generic() => MediatorScopedGeneric.Send(_mediatorScoped);

    [Benchmark]
    public Task<Pong> MediatR_Generic() => MediatR.Send(_mediatr);

    [Benchmark]
    public ValueTask<Pong> ImmediateHandlers_Typed() => _immediateHandler.HandleAsync(_immediate);

    [Benchmark]
    public ValueTask<Pong> ZeroAlloc_IMediator() => ZeroAllocMediator.Send(_zeroAlloc);
}

/// <summary>
///     <see cref="RealWorldBehaviors" /> with a scope per request: create a scope, resolve the entry point, send, dispose. Every scoped
///     service (repository, validator, and Keryx's pipeline context) is created per request. Same n/a subjects.
/// </summary>
[Expect(Result = PongCatalog.BenchmarkId, Name = PongCatalog.BenchmarkName, Hits = 7, BaselineHits = 5)]
public class RealWorldBehaviorsScopePerRequest : ComparisonBenchmark
{
    private readonly B.TypicalPing _baseline = new(PongCatalog.BenchmarkId);
    private readonly IH.TypicalPing _immediate = new(PongCatalog.BenchmarkId);
    private readonly K.TypicalPing _keryx = new(PongCatalog.BenchmarkId);
    private readonly MR.TypicalPing _mediatr = new(PongCatalog.BenchmarkId);
    private readonly MS.TypicalPing _mediatorScoped = new(PongCatalog.BenchmarkId);
    private readonly ZA.TypicalPing _zeroAlloc = new(PongCatalog.BenchmarkId);
    private ScopeFactories _scopes = null!;

    private protected override void Resolve(Subjects subjects) => _scopes = new(subjects);

    [Benchmark(Baseline = true)]
    public async ValueTask<Pong> Baseline()
    {
        using var scope = _scopes.Baseline.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<B.TypicalPingHandler>().Handle(_baseline);
    }

    [Benchmark]
    public async ValueTask<Pong> Keryx_Typed()
    {
        using var scope = _scopes.Keryx.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<K.KeryxMediator>().Send(_keryx);
    }

    [Benchmark]
    public async ValueTask<Pong> Keryx_Generic()
    {
        using var scope = _scopes.Keryx.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(_keryx);
    }

    [Benchmark]
    public async ValueTask<Pong> MediatorScoped_Typed()
    {
        using var scope = _scopes.MediatorScoped.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MS.Mediator>().Send(_mediatorScoped);
    }

    [Benchmark]
    public async ValueTask<Pong> MediatorScoped_Generic()
    {
        using var scope = _scopes.MediatorScoped.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<Mediator.IMediator>().Send(_mediatorScoped);
    }

    [Benchmark]
    public async ValueTask<Pong> MediatR_Generic()
    {
        using var scope = _scopes.MediatR.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MediatR.IMediator>().Send(_mediatr);
    }

    [Benchmark]
    public async ValueTask<Pong> ImmediateHandlers_Typed()
    {
        using var scope = _scopes.ImmediateHandlers.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IH.TypicalPingHandler.Handler>().HandleAsync(_immediate);
    }

    [Benchmark]
    public async ValueTask<Pong> ZeroAlloc_IMediator()
    {
        using var scope = _scopes.ZeroAlloc.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ZeroAlloc.Mediator.IMediator>().Send(_zeroAlloc);
    }
}

/// <summary>
///     <c>Send(QueryPing)</c> to a handler that runs a parameterized scalar query against the in-memory SQLite database
///     (<see cref="PongDatabase" />) on the container's singleton open connection, from the long-lived scope. SQLite's async methods complete
///     synchronously. ZeroAlloc.Mediator's static <c>Mediator</c> gets the handler from the factory
///     <see cref="ZA.ZeroAllocSetup.UseStaticFactories" /> registers.
/// </summary>
[Expect(Result = PongCatalog.BenchmarkId, Name = PongCatalog.BenchmarkName, Hits = 2)]
public class RealWorldSqlite : ComparisonBenchmark
{
    private readonly B.QueryPing _baseline = new(PongCatalog.BenchmarkId);
    private readonly IH.QueryPing _immediate = new(PongCatalog.BenchmarkId);
    private readonly K.QueryPing _keryx = new(PongCatalog.BenchmarkId);
    private readonly MR.QueryPing _mediatr = new(PongCatalog.BenchmarkId);
    private readonly MS.QueryPing _mediatorScoped = new(PongCatalog.BenchmarkId);
    private readonly MSi.QueryPing _mediatorSingleton = new(PongCatalog.BenchmarkId);
    private readonly ZA.QueryPing _zeroAlloc = new(PongCatalog.BenchmarkId);
    private B.QueryPingHandler _baselineHandler = null!;
    private IH.QueryPingHandler.Handler _immediateHandler = null!;

    private protected override void Resolve(Subjects subjects)
    {
        _baselineHandler = subjects.LongLived(Library.Baseline).GetRequiredService<B.QueryPingHandler>();
        _immediateHandler = subjects.Immediate<IH.QueryPingHandler.Handler>();
        ZA.ZeroAllocSetup.UseStaticFactories(subjects.LongLived(Library.ZeroAlloc));
    }

    [Benchmark(Baseline = true)]
    public ValueTask<Pong> Baseline() => _baselineHandler.Handle(_baseline);

    [Benchmark]
    public ValueTask<Pong> Keryx_Typed() => KeryxTyped.Send(_keryx);

    [Benchmark]
    public ValueTask<Pong> Keryx_Generic() => KeryxGeneric.Send(_keryx);

    [Benchmark]
    public ValueTask<Pong> MediatorScoped_Typed() => MediatorScopedTyped.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<Pong> MediatorScoped_Generic() => MediatorScopedGeneric.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<Pong> MediatorSingleton_Typed() => MediatorSingletonTyped.Send(_mediatorSingleton);

    [Benchmark]
    public ValueTask<Pong> MediatorSingleton_Generic() => MediatorSingletonGeneric.Send(_mediatorSingleton);

    [Benchmark]
    public Task<Pong> MediatR_Generic() => MediatR.Send(_mediatr);

    [Benchmark]
    public ValueTask<Pong> ImmediateHandlers_Typed() => _immediateHandler.HandleAsync(_immediate);

    [Benchmark]
    public ValueTask<Pong> ZeroAlloc_Static() => ZeroAlloc.Mediator.Mediator.Send(_zeroAlloc);

    [Benchmark]
    public ValueTask<Pong> ZeroAlloc_IMediator() => ZeroAllocMediator.Send(_zeroAlloc);
}

/// <summary>Each container's scope factory, read once in setup, as ASP.NET Core holds it; a scope-per-request benchmark reads it from here.</summary>
internal sealed class ScopeFactories(Subjects subjects)
{
    public readonly IServiceScopeFactory Baseline = subjects.Scopes(Library.Baseline);
    public readonly IServiceScopeFactory Keryx = subjects.Scopes(Library.Keryx);
    public readonly IServiceScopeFactory MediatorScoped = subjects.Scopes(Library.MediatorScoped);
    public readonly IServiceScopeFactory MediatorSingleton = subjects.Scopes(Library.MediatorSingleton);
    public readonly IServiceScopeFactory MediatR = subjects.Scopes(Library.MediatR);
    public readonly IServiceScopeFactory ImmediateHandlers = subjects.Scopes(Library.ImmediateHandlers);
    public readonly IServiceScopeFactory ZeroAlloc = subjects.Scopes(Library.ZeroAlloc);
}
