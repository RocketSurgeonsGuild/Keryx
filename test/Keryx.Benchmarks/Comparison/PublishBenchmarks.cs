// Sequential publish (every library's default) to handlers that complete synchronously. Immediate.Handlers has no notifications.
using BenchmarkDotNet.Attributes;
using B = Keryx.Benchmarks.Baseline;
using K = Keryx.Benchmarks.KeryxHost;
using MR = Keryx.Benchmarks.MediatRHandlers;
using MS = Keryx.Benchmarks.MediatorScoped;
using MSi = Keryx.Benchmarks.MediatorSingleton;
using ZA = Keryx.Benchmarks.ZeroAllocMediator;

namespace Keryx.Benchmarks.Comparison;

/// <summary><c>Publish</c> to 1 handler.</summary>
[Expect(Hits = 1)]
public class PublishTo1 : ComparisonBenchmark
{
    private readonly B.Pinged1 _baseline = new(21);
    private readonly K.Pinged1 _keryx = new(21);
    private readonly MR.Pinged1 _mediatr = new(21);
    private readonly MS.Pinged1 _mediatorScoped = new(21);
    private readonly MSi.Pinged1 _mediatorSingleton = new(21);
    private readonly ZA.Pinged1 _zeroAlloc = new(21);

    [Benchmark(Baseline = true)]
    public ValueTask Baseline() => B.Handlers.Publish(_baseline);

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

/// <summary><c>Publish</c> to 5 handlers.</summary>
[Expect(Hits = 5)]
public class PublishTo5 : ComparisonBenchmark
{
    private readonly B.Pinged5 _baseline = new(21);
    private readonly K.Pinged5 _keryx = new(21);
    private readonly MR.Pinged5 _mediatr = new(21);
    private readonly MS.Pinged5 _mediatorScoped = new(21);
    private readonly MSi.Pinged5 _mediatorSingleton = new(21);
    private readonly ZA.Pinged5 _zeroAlloc = new(21);

    [Benchmark(Baseline = true)]
    public ValueTask Baseline() => B.Handlers.Publish(_baseline);

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
