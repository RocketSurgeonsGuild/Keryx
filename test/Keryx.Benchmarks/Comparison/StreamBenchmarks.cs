// Each benchmark drains a stream of 100 items (0..99) and returns the sum.
using BenchmarkDotNet.Attributes;
using Keryx.Benchmarks.Common;
using B = Keryx.Benchmarks.Baseline;
using IH = Keryx.Benchmarks.ImmediateHandlers;
using K = Keryx.Benchmarks.KeryxHost;
using MR = Keryx.Benchmarks.MediatRHandlers;
using MS = Keryx.Benchmarks.MediatorScoped;
using MSi = Keryx.Benchmarks.MediatorSingleton;
using ZA = Keryx.Benchmarks.ZeroAllocMediator;

namespace Keryx.Benchmarks.Comparison;

/// <summary><c>Stream</c> of 100 items with no stream behaviors.</summary>
[Expect(Result = 4950, Hits = 1)]
public class StreamDepth0 : ComparisonBenchmark
{
    private readonly B.Count0 _baseline = new(Numbers.StreamLength);
    private readonly IH.Count0 _immediate = new(Numbers.StreamLength);
    private readonly K.Count0 _keryx = new(Numbers.StreamLength);
    private readonly MR.Count0 _mediatr = new(Numbers.StreamLength);
    private readonly MS.Count0 _mediatorScoped = new(Numbers.StreamLength);
    private readonly MSi.Count0 _mediatorSingleton = new(Numbers.StreamLength);
    private readonly ZA.Count0 _zeroAlloc = new(Numbers.StreamLength);
    private IH.Count0Handler.Handler _immediateHandler = null!;

    private protected override void Resolve(Subjects subjects) => _immediateHandler = subjects.Immediate<IH.Count0Handler.Handler>();

    [Benchmark(Baseline = true)]
    public ValueTask<int> Baseline() => Drain.Sum(B.Handlers.Handle(_baseline));

    [Benchmark]
    public ValueTask<int> Keryx_Typed() => Drain.Sum(KeryxTyped.Stream(_keryx));

    [Benchmark]
    public ValueTask<int> Keryx_Generic() => Drain.Sum(KeryxGeneric.Stream(_keryx));

    [Benchmark]
    public ValueTask<int> MediatorScoped_Typed() => Drain.Sum(MediatorScopedTyped.CreateStream(_mediatorScoped));

    [Benchmark]
    public ValueTask<int> MediatorScoped_Generic() => Drain.Sum(MediatorScopedGeneric.CreateStream(_mediatorScoped));

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Typed() => Drain.Sum(MediatorSingletonTyped.CreateStream(_mediatorSingleton));

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Generic() => Drain.Sum(MediatorSingletonGeneric.CreateStream(_mediatorSingleton));

    [Benchmark]
    public ValueTask<int> MediatR_Generic() => Drain.Sum(MediatR.CreateStream(_mediatr));

    [Benchmark]
    public ValueTask<int> ImmediateHandlers_Typed() => Drain.Sum(_immediateHandler.HandleAsync(_immediate));

    [Benchmark]
    public ValueTask<int> ZeroAlloc_Static() => Drain.Sum(ZeroAlloc.Mediator.Mediator.CreateStream(_zeroAlloc));

    [Benchmark]
    public ValueTask<int> ZeroAlloc_IMediator() => Drain.Sum(ZeroAllocMediator.CreateStream(_zeroAlloc));
}

/// <summary>
///     <c>Stream</c> of 100 items through 3 pass-through stream behaviors that re-yield the inner stream (for Keryx, static
///     <c>[StreamBehavior]</c>s, one iterator each, the shape the other libraries use). ZeroAlloc.Mediator has no stream behaviors.
/// </summary>
[Expect(Result = 4950, Hits = 4, BaselineHits = 1)]
public class StreamDepth3 : ComparisonBenchmark
{
    private readonly B.Count3 _baseline = new(Numbers.StreamLength);
    private readonly IH.Count3 _immediate = new(Numbers.StreamLength);
    private readonly K.Count3 _keryx = new(Numbers.StreamLength);
    private readonly MR.Count3 _mediatr = new(Numbers.StreamLength);
    private readonly MS.Count3 _mediatorScoped = new(Numbers.StreamLength);
    private readonly MSi.Count3 _mediatorSingleton = new(Numbers.StreamLength);
    private IH.Count3Handler.Handler _immediateHandler = null!;

    private protected override void Resolve(Subjects subjects) => _immediateHandler = subjects.Immediate<IH.Count3Handler.Handler>();

    [Benchmark(Baseline = true)]
    public ValueTask<int> Baseline() => Drain.Sum(B.Handlers.Handle(_baseline));

    [Benchmark]
    public ValueTask<int> Keryx_Typed() => Drain.Sum(KeryxTyped.Stream(_keryx));

    [Benchmark]
    public ValueTask<int> Keryx_Generic() => Drain.Sum(KeryxGeneric.Stream(_keryx));

    [Benchmark]
    public ValueTask<int> MediatorScoped_Typed() => Drain.Sum(MediatorScopedTyped.CreateStream(_mediatorScoped));

    [Benchmark]
    public ValueTask<int> MediatorScoped_Generic() => Drain.Sum(MediatorScopedGeneric.CreateStream(_mediatorScoped));

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Typed() => Drain.Sum(MediatorSingletonTyped.CreateStream(_mediatorSingleton));

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Generic() => Drain.Sum(MediatorSingletonGeneric.CreateStream(_mediatorSingleton));

    [Benchmark]
    public ValueTask<int> MediatR_Generic() => Drain.Sum(MediatR.CreateStream(_mediatr));

    [Benchmark]
    public ValueTask<int> ImmediateHandlers_Typed() => Drain.Sum(_immediateHandler.HandleAsync(_immediate));
}
