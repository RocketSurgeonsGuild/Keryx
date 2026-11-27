// The real-world notification scenarios (ADR 0001, Benchmarks; owner decision 30d, 2026-11-27): a Pinged notification published to 1 and 5
// handlers that do realistic work (PingedWork, in Keryx.Benchmarks.Common, the same body in every subject). "Scope per request" classes create a
// scope, resolve the entry point, publish and dispose the scope inside the measured method; each handler looks the pong up in the scoped
// repository and logs it. The "Behavior" pair adds the notification logging behavior (two messages per publish) for the libraries that have
// notification behaviors. The "Async" and "Parallel" classes publish from the long-lived scope to handlers that await the SQLite query and log,
// with the library's default sequential strategy and with its parallel one. The baseline is a hand-written publisher class resolved from DI that
// calls hand-written handler classes with the same bodies. A subject a scenario cannot run is left out (n/a, with the reason on the class and
// in open-questions OQ-72). Immediate.Handlers has no notifications.
using BenchmarkDotNet.Attributes;
using Keryx.Benchmarks.Common;
using Microsoft.Extensions.DependencyInjection;
using B = Keryx.Benchmarks.Baseline;
using K = Keryx.Benchmarks.KeryxHost;
using MP = Keryx.Benchmarks.MediatorParallel;
using MR = Keryx.Benchmarks.MediatRHandlers;
using MS = Keryx.Benchmarks.MediatorScoped;
using MSi = Keryx.Benchmarks.MediatorSingleton;
using ZA = Keryx.Benchmarks.ZeroAllocMediator;

namespace Keryx.Benchmarks.Comparison;

/// <summary>
///     <c>Publish(TypicalPinged1)</c> with a scope per request: create a scope, resolve the entry point, publish to 1 handler (each looks the
///     pong up in the scoped repository and logs it), dispose. n/a: Mediator singleton (its singleton handlers cannot take the scoped
///     repository) and ZeroAlloc.Mediator's static <c>Mediator</c> (it has no scope).
/// </summary>
[Expect(Hits = 3)]
public class RealWorldPublish1ScopePerRequest : ComparisonBenchmark
{
    private readonly B.TypicalPinged1 _baseline = new(PongCatalog.BenchmarkId);
    private readonly K.TypicalPinged1 _keryx = new(PongCatalog.BenchmarkId);
    private readonly MR.TypicalPinged1 _mediatr = new(PongCatalog.BenchmarkId);
    private readonly MS.TypicalPinged1 _mediatorScoped = new(PongCatalog.BenchmarkId);
    private readonly ZA.TypicalPinged1 _zeroAlloc = new(PongCatalog.BenchmarkId);
    private ScopeFactories _scopes = null!;

    private protected override void Resolve(Subjects subjects) => _scopes = new(subjects);

    [Benchmark(Baseline = true)]
    public async ValueTask Baseline()
    {
        using var scope = _scopes.Baseline.CreateScope();
        await scope.ServiceProvider.GetRequiredService<B.TypicalPinged1Publisher>().Publish(_baseline);
    }

    [Benchmark]
    public async ValueTask Keryx_Typed()
    {
        using var scope = _scopes.Keryx.CreateScope();
        await scope.ServiceProvider.GetRequiredService<K.KeryxMediator>().Publish(_keryx);
    }

    [Benchmark]
    public async ValueTask Keryx_Generic()
    {
        using var scope = _scopes.Keryx.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Publish(_keryx);
    }

    [Benchmark]
    public async ValueTask MediatorScoped_Typed()
    {
        using var scope = _scopes.MediatorScoped.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MS.Mediator>().Publish(_mediatorScoped);
    }

    [Benchmark]
    public async ValueTask MediatorScoped_Generic()
    {
        using var scope = _scopes.MediatorScoped.CreateScope();
        await scope.ServiceProvider.GetRequiredService<Mediator.IMediator>().Publish(_mediatorScoped);
    }

    [Benchmark]
    public async ValueTask MediatR_Generic()
    {
        using var scope = _scopes.MediatR.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MediatR.IMediator>().Publish(_mediatr);
    }

    [Benchmark]
    public async ValueTask ZeroAlloc_IMediator()
    {
        using var scope = _scopes.ZeroAlloc.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ZeroAlloc.Mediator.IMediator>().Publish(_zeroAlloc);
    }
}

/// <summary>
///     <c>Publish(TypicalPinged5)</c> with a scope per request: create a scope, resolve the entry point, publish to 5 handlers (each looks the
///     pong up in the scoped repository and logs it), dispose. n/a: Mediator singleton (its singleton handlers cannot take the scoped
///     repository) and ZeroAlloc.Mediator's static <c>Mediator</c> (it has no scope).
/// </summary>
[Expect(Hits = 15)]
public class RealWorldPublish5ScopePerRequest : ComparisonBenchmark
{
    private readonly B.TypicalPinged5 _baseline = new(PongCatalog.BenchmarkId);
    private readonly K.TypicalPinged5 _keryx = new(PongCatalog.BenchmarkId);
    private readonly MR.TypicalPinged5 _mediatr = new(PongCatalog.BenchmarkId);
    private readonly MS.TypicalPinged5 _mediatorScoped = new(PongCatalog.BenchmarkId);
    private readonly ZA.TypicalPinged5 _zeroAlloc = new(PongCatalog.BenchmarkId);
    private ScopeFactories _scopes = null!;

    private protected override void Resolve(Subjects subjects) => _scopes = new(subjects);

    [Benchmark(Baseline = true)]
    public async ValueTask Baseline()
    {
        using var scope = _scopes.Baseline.CreateScope();
        await scope.ServiceProvider.GetRequiredService<B.TypicalPinged5Publisher>().Publish(_baseline);
    }

    [Benchmark]
    public async ValueTask Keryx_Typed()
    {
        using var scope = _scopes.Keryx.CreateScope();
        await scope.ServiceProvider.GetRequiredService<K.KeryxMediator>().Publish(_keryx);
    }

    [Benchmark]
    public async ValueTask Keryx_Generic()
    {
        using var scope = _scopes.Keryx.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Publish(_keryx);
    }

    [Benchmark]
    public async ValueTask MediatorScoped_Typed()
    {
        using var scope = _scopes.MediatorScoped.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MS.Mediator>().Publish(_mediatorScoped);
    }

    [Benchmark]
    public async ValueTask MediatorScoped_Generic()
    {
        using var scope = _scopes.MediatorScoped.CreateScope();
        await scope.ServiceProvider.GetRequiredService<Mediator.IMediator>().Publish(_mediatorScoped);
    }

    [Benchmark]
    public async ValueTask MediatR_Generic()
    {
        using var scope = _scopes.MediatR.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MediatR.IMediator>().Publish(_mediatr);
    }

    [Benchmark]
    public async ValueTask ZeroAlloc_IMediator()
    {
        using var scope = _scopes.ZeroAlloc.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ZeroAlloc.Mediator.IMediator>().Publish(_zeroAlloc);
    }
}

/// <summary>
///     <see cref="RealWorldPublish1ScopePerRequest" /> with the notification logging behavior around the handlers (two formatted messages per
///     publish); the baseline writes the two messages inline. Only Keryx has notification behaviors: Mediator's and ZeroAlloc.Mediator's
///     pipeline behaviors wrap requests only, MediatR's wrap <c>Send</c> only, and Immediate.Handlers has no notifications (n/a).
/// </summary>
[Expect(Hits = 6, BaselineHits = 5)]
public class RealWorldPublishBehavior1ScopePerRequest : ComparisonBenchmark
{
    private readonly B.TypicalPinged1 _baseline = new(PongCatalog.BenchmarkId);
    private readonly K.LoggedPinged1 _keryx = new(PongCatalog.BenchmarkId);
    private ScopeFactories _scopes = null!;

    private protected override void Resolve(Subjects subjects) => _scopes = new(subjects);

    [Benchmark(Baseline = true)]
    public async ValueTask Baseline()
    {
        using var scope = _scopes.Baseline.CreateScope();
        await scope.ServiceProvider.GetRequiredService<B.LoggedPinged1Publisher>().Publish(_baseline);
    }

    [Benchmark]
    public async ValueTask Keryx_Typed()
    {
        using var scope = _scopes.Keryx.CreateScope();
        await scope.ServiceProvider.GetRequiredService<K.KeryxMediator>().Publish(_keryx);
    }

    [Benchmark]
    public async ValueTask Keryx_Generic()
    {
        using var scope = _scopes.Keryx.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Publish(_keryx);
    }
}

/// <summary>
///     <see cref="RealWorldPublish5ScopePerRequest" /> with the notification logging behavior around the handlers (two formatted messages per
///     publish); the baseline writes the two messages inline. Only Keryx has notification behaviors: Mediator's and ZeroAlloc.Mediator's
///     pipeline behaviors wrap requests only, MediatR's wrap <c>Send</c> only, and Immediate.Handlers has no notifications (n/a).
/// </summary>
[Expect(Hits = 18, BaselineHits = 17)]
public class RealWorldPublishBehavior5ScopePerRequest : ComparisonBenchmark
{
    private readonly B.TypicalPinged5 _baseline = new(PongCatalog.BenchmarkId);
    private readonly K.LoggedPinged5 _keryx = new(PongCatalog.BenchmarkId);
    private ScopeFactories _scopes = null!;

    private protected override void Resolve(Subjects subjects) => _scopes = new(subjects);

    [Benchmark(Baseline = true)]
    public async ValueTask Baseline()
    {
        using var scope = _scopes.Baseline.CreateScope();
        await scope.ServiceProvider.GetRequiredService<B.LoggedPinged5Publisher>().Publish(_baseline);
    }

    [Benchmark]
    public async ValueTask Keryx_Typed()
    {
        using var scope = _scopes.Keryx.CreateScope();
        await scope.ServiceProvider.GetRequiredService<K.KeryxMediator>().Publish(_keryx);
    }

    [Benchmark]
    public async ValueTask Keryx_Generic()
    {
        using var scope = _scopes.Keryx.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Publish(_keryx);
    }
}

/// <summary>
///     <c>Publish(QueryPinged1)</c> to 1 async handler (each awaits the SQLite query, then logs), with each library's default sequential
///     strategy, from the long-lived scope. SQLite's async methods complete synchronously. ZeroAlloc.Mediator's static <c>Mediator</c> gets
///     the handlers from the factories <see cref="ZA.ZeroAllocSetup.UseStaticFactories" /> registers.
/// </summary>
[Expect(Hits = 3)]
public class RealWorldPublishAsync1 : ComparisonBenchmark
{
    private readonly B.QueryPinged1 _baseline = new(PongCatalog.BenchmarkId);
    private readonly K.QueryPinged1 _keryx = new(PongCatalog.BenchmarkId);
    private readonly MR.QueryPinged1 _mediatr = new(PongCatalog.BenchmarkId);
    private readonly MS.QueryPinged1 _mediatorScoped = new(PongCatalog.BenchmarkId);
    private readonly MSi.QueryPinged1 _mediatorSingleton = new(PongCatalog.BenchmarkId);
    private readonly ZA.QueryPinged1 _zeroAlloc = new(PongCatalog.BenchmarkId);
    private B.QueryPinged1Publisher _baselinePublisher = null!;

    private protected override void Resolve(Subjects subjects)
    {
        _baselinePublisher = subjects.LongLived(Library.Baseline).GetRequiredService<B.QueryPinged1Publisher>();
        ZA.ZeroAllocSetup.UseStaticFactories(subjects.LongLived(Library.ZeroAlloc));
    }

    [Benchmark(Baseline = true)]
    public ValueTask Baseline() => _baselinePublisher.Publish(_baseline);

    [Benchmark]
    public ValueTask Keryx_Typed() => KeryxTyped.Publish(_keryx);

    [Benchmark]
    public ValueTask Keryx_Generic() => KeryxGeneric.Publish(_keryx);

    [Benchmark]
    public ValueTask MediatorScoped_Typed() => MediatorScopedTyped.Publish(_mediatorScoped);

    [Benchmark]
    public ValueTask MediatorScoped_Generic() => MediatorScopedGeneric.Publish(_mediatorScoped);

    [Benchmark]
    public ValueTask MediatorSingleton_Typed() => MediatorSingletonTyped.Publish(_mediatorSingleton);

    [Benchmark]
    public ValueTask MediatorSingleton_Generic() => MediatorSingletonGeneric.Publish(_mediatorSingleton);

    [Benchmark]
    public Task MediatR_Generic() => MediatR.Publish(_mediatr);

    [Benchmark]
    public ValueTask ZeroAlloc_Static() => ZeroAlloc.Mediator.Mediator.Publish(_zeroAlloc);

    [Benchmark]
    public ValueTask ZeroAlloc_IMediator() => ZeroAllocMediator.Publish(_zeroAlloc);
}

/// <summary>
///     <c>Publish(QueryPinged5)</c> to 5 async handlers (each awaits the SQLite query, then logs), with each library's default sequential
///     strategy, from the long-lived scope. SQLite's async methods complete synchronously. ZeroAlloc.Mediator's static <c>Mediator</c> gets
///     the handlers from the factories <see cref="ZA.ZeroAllocSetup.UseStaticFactories" /> registers.
/// </summary>
[Expect(Hits = 15)]
public class RealWorldPublishAsync5 : ComparisonBenchmark
{
    private readonly B.QueryPinged5 _baseline = new(PongCatalog.BenchmarkId);
    private readonly K.QueryPinged5 _keryx = new(PongCatalog.BenchmarkId);
    private readonly MR.QueryPinged5 _mediatr = new(PongCatalog.BenchmarkId);
    private readonly MS.QueryPinged5 _mediatorScoped = new(PongCatalog.BenchmarkId);
    private readonly MSi.QueryPinged5 _mediatorSingleton = new(PongCatalog.BenchmarkId);
    private readonly ZA.QueryPinged5 _zeroAlloc = new(PongCatalog.BenchmarkId);
    private B.QueryPinged5Publisher _baselinePublisher = null!;

    private protected override void Resolve(Subjects subjects)
    {
        _baselinePublisher = subjects.LongLived(Library.Baseline).GetRequiredService<B.QueryPinged5Publisher>();
        ZA.ZeroAllocSetup.UseStaticFactories(subjects.LongLived(Library.ZeroAlloc));
    }

    [Benchmark(Baseline = true)]
    public ValueTask Baseline() => _baselinePublisher.Publish(_baseline);

    [Benchmark]
    public ValueTask Keryx_Typed() => KeryxTyped.Publish(_keryx);

    [Benchmark]
    public ValueTask Keryx_Generic() => KeryxGeneric.Publish(_keryx);

    [Benchmark]
    public ValueTask MediatorScoped_Typed() => MediatorScopedTyped.Publish(_mediatorScoped);

    [Benchmark]
    public ValueTask MediatorScoped_Generic() => MediatorScopedGeneric.Publish(_mediatorScoped);

    [Benchmark]
    public ValueTask MediatorSingleton_Typed() => MediatorSingletonTyped.Publish(_mediatorSingleton);

    [Benchmark]
    public ValueTask MediatorSingleton_Generic() => MediatorSingletonGeneric.Publish(_mediatorSingleton);

    [Benchmark]
    public Task MediatR_Generic() => MediatR.Publish(_mediatr);

    [Benchmark]
    public ValueTask ZeroAlloc_Static() => ZeroAlloc.Mediator.Mediator.Publish(_zeroAlloc);

    [Benchmark]
    public ValueTask ZeroAlloc_IMediator() => ZeroAllocMediator.Publish(_zeroAlloc);
}

/// <summary>
///     <see cref="RealWorldPublishAsync1" /> with each library's parallel publish strategy: Keryx's <c>PublishStrategy.Parallel</c> (on the
///     notification type), Mediator's <c>TaskWhenAllPublisher</c> (its own assembly, singleton lifetime), MediatR's <c>TaskWhenAllPublisher</c>
///     (its own container) and ZeroAlloc.Mediator's <c>[ParallelNotification]</c>; the baseline starts every handler before awaiting any.
///     Mediator scoped has no second row: the publisher is per assembly, and one Mediator assembly carries it.
/// </summary>
[Expect(Hits = 3)]
public class RealWorldPublishParallel1 : ComparisonBenchmark
{
    private readonly B.QueryPinged1 _baseline = new(PongCatalog.BenchmarkId);
    private readonly K.ParallelQueryPinged1 _keryx = new(PongCatalog.BenchmarkId);
    private readonly MR.QueryPinged1 _mediatr = new(PongCatalog.BenchmarkId);
    private readonly MP.QueryPinged1 _mediatorParallel = new(PongCatalog.BenchmarkId);
    private readonly ZA.ParallelQueryPinged1 _zeroAlloc = new(PongCatalog.BenchmarkId);
    private B.QueryPinged1Publisher _baselinePublisher = null!;
    private MP.Mediator _mediatorParallelTyped = null!;
    private Mediator.IMediator _mediatorParallelGeneric = null!;
    private MediatR.IMediator _mediatrParallel = null!;

    private protected override void Resolve(Subjects subjects)
    {
        _baselinePublisher = subjects.LongLived(Library.Baseline).GetRequiredService<B.QueryPinged1Publisher>();
        _mediatorParallelTyped = subjects.MediatorParallelTyped;
        _mediatorParallelGeneric = subjects.MediatorParallelGeneric;
        _mediatrParallel = subjects.MediatRParallel;
        ZA.ZeroAllocSetup.UseStaticFactories(subjects.LongLived(Library.ZeroAlloc));
    }

    [Benchmark(Baseline = true)]
    public ValueTask Baseline() => _baselinePublisher.Publish(_baseline);

    [Benchmark]
    public ValueTask Keryx_Typed() => KeryxTyped.Publish(_keryx);

    [Benchmark]
    public ValueTask Keryx_Generic() => KeryxGeneric.Publish(_keryx);

    [Benchmark]
    public ValueTask MediatorSingleton_Typed() => _mediatorParallelTyped.Publish(_mediatorParallel);

    [Benchmark]
    public ValueTask MediatorSingleton_Generic() => _mediatorParallelGeneric.Publish(_mediatorParallel);

    [Benchmark]
    public Task MediatR_Generic() => _mediatrParallel.Publish(_mediatr);

    [Benchmark]
    public ValueTask ZeroAlloc_Static() => ZeroAlloc.Mediator.Mediator.Publish(_zeroAlloc);

    [Benchmark]
    public ValueTask ZeroAlloc_IMediator() => ZeroAllocMediator.Publish(_zeroAlloc);
}

/// <summary>
///     <see cref="RealWorldPublishAsync5" /> with each library's parallel publish strategy: Keryx's <c>PublishStrategy.Parallel</c> (on the
///     notification type), Mediator's <c>TaskWhenAllPublisher</c> (its own assembly, singleton lifetime), MediatR's <c>TaskWhenAllPublisher</c>
///     (its own container) and ZeroAlloc.Mediator's <c>[ParallelNotification]</c>; the baseline starts every handler before awaiting any.
///     Mediator scoped has no second row: the publisher is per assembly, and one Mediator assembly carries it.
/// </summary>
[Expect(Hits = 15)]
public class RealWorldPublishParallel5 : ComparisonBenchmark
{
    private readonly B.QueryPinged5 _baseline = new(PongCatalog.BenchmarkId);
    private readonly K.ParallelQueryPinged5 _keryx = new(PongCatalog.BenchmarkId);
    private readonly MR.QueryPinged5 _mediatr = new(PongCatalog.BenchmarkId);
    private readonly MP.QueryPinged5 _mediatorParallel = new(PongCatalog.BenchmarkId);
    private readonly ZA.ParallelQueryPinged5 _zeroAlloc = new(PongCatalog.BenchmarkId);
    private B.QueryPinged5Publisher _baselinePublisher = null!;
    private MP.Mediator _mediatorParallelTyped = null!;
    private Mediator.IMediator _mediatorParallelGeneric = null!;
    private MediatR.IMediator _mediatrParallel = null!;

    private protected override void Resolve(Subjects subjects)
    {
        _baselinePublisher = subjects.LongLived(Library.Baseline).GetRequiredService<B.QueryPinged5Publisher>();
        _mediatorParallelTyped = subjects.MediatorParallelTyped;
        _mediatorParallelGeneric = subjects.MediatorParallelGeneric;
        _mediatrParallel = subjects.MediatRParallel;
        ZA.ZeroAllocSetup.UseStaticFactories(subjects.LongLived(Library.ZeroAlloc));
    }

    [Benchmark(Baseline = true)]
    public ValueTask Baseline() => _baselinePublisher.PublishParallel(_baseline);

    [Benchmark]
    public ValueTask Keryx_Typed() => KeryxTyped.Publish(_keryx);

    [Benchmark]
    public ValueTask Keryx_Generic() => KeryxGeneric.Publish(_keryx);

    [Benchmark]
    public ValueTask MediatorSingleton_Typed() => _mediatorParallelTyped.Publish(_mediatorParallel);

    [Benchmark]
    public ValueTask MediatorSingleton_Generic() => _mediatorParallelGeneric.Publish(_mediatorParallel);

    [Benchmark]
    public Task MediatR_Generic() => _mediatrParallel.Publish(_mediatr);

    [Benchmark]
    public ValueTask ZeroAlloc_Static() => ZeroAlloc.Mediator.Mediator.Publish(_zeroAlloc);

    [Benchmark]
    public ValueTask ZeroAlloc_IMediator() => ZeroAllocMediator.Publish(_zeroAlloc);
}
