// Written out per scenario on purpose: every benchmark makes one direct call, with no switch over the depth in the measured code.
using BenchmarkDotNet.Attributes;
using B = Keryx.Benchmarks.Baseline;
using IH = Keryx.Benchmarks.ImmediateHandlers;
using K = Keryx.Benchmarks.KeryxHost;
using MR = Keryx.Benchmarks.MediatRHandlers;
using MS = Keryx.Benchmarks.MediatorScoped;
using MSi = Keryx.Benchmarks.MediatorSingleton;
using ZA = Keryx.Benchmarks.ZeroAllocMediator;

namespace Keryx.Benchmarks.Comparison;

/// <summary><c>Send</c> to a synchronous handler through 0 pass-through behaviors.</summary>
[Expect(Result = 42, Hits = 1, BaselineHits = 1)]
public class SendSyncDepth0 : ComparisonBenchmark
{
    private readonly B.Ping0 _baseline = new(21);
    private readonly IH.Ping0 _immediate = new(21);
    private readonly K.Ping0 _keryx = new(21);
    private readonly MR.Ping0 _mediatr = new(21);
    private readonly MS.Ping0 _mediatorScoped = new(21);
    private readonly MSi.Ping0 _mediatorSingleton = new(21);
    private readonly ZA.Ping0 _zeroAlloc = new(21);
    private IH.Ping0Handler.Handler _immediateHandler = null!;

    private protected override void Resolve(Subjects subjects) => _immediateHandler = subjects.Immediate<IH.Ping0Handler.Handler>();

    [Benchmark(Baseline = true)]
    public ValueTask<int> Baseline() => B.Handlers.Handle(_baseline);

    [Benchmark]
    public ValueTask<int> Keryx_Typed() => KeryxTyped.Send(_keryx);

    [Benchmark]
    public ValueTask<int> Keryx_Generic() => KeryxGeneric.Send(_keryx);

    [Benchmark]
    public ValueTask<int> MediatorScoped_Typed() => MediatorScopedTyped.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<int> MediatorScoped_Generic() => MediatorScopedGeneric.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Typed() => MediatorSingletonTyped.Send(_mediatorSingleton);

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Generic() => MediatorSingletonGeneric.Send(_mediatorSingleton);

    [Benchmark]
    public Task<int> MediatR_Generic() => MediatR.Send(_mediatr);

    [Benchmark]
    public ValueTask<int> ImmediateHandlers_Typed() => _immediateHandler.HandleAsync(_immediate);

    [Benchmark]
    public ValueTask<int> ZeroAlloc_Static() => ZeroAlloc.Mediator.Mediator.Send(_zeroAlloc);

    [Benchmark]
    public ValueTask<int> ZeroAlloc_IMediator() => ZeroAllocMediator.Send(_zeroAlloc);
}

/// <summary><c>Send</c> to a synchronous handler through 1 pass-through behavior.</summary>
[Expect(Result = 42, Hits = 2, BaselineHits = 1)]
public class SendSyncDepth1 : ComparisonBenchmark
{
    private readonly B.Ping1 _baseline = new(21);
    private readonly IH.Ping1 _immediate = new(21);
    private readonly K.Ping1 _keryx = new(21);
    private readonly MR.Ping1 _mediatr = new(21);
    private readonly MS.Ping1 _mediatorScoped = new(21);
    private readonly MSi.Ping1 _mediatorSingleton = new(21);
    private readonly ZA.Ping1 _zeroAlloc = new(21);
    private IH.Ping1Handler.Handler _immediateHandler = null!;

    private protected override void Resolve(Subjects subjects) => _immediateHandler = subjects.Immediate<IH.Ping1Handler.Handler>();

    [Benchmark(Baseline = true)]
    public ValueTask<int> Baseline() => B.Handlers.Handle(_baseline);

    [Benchmark]
    public ValueTask<int> Keryx_Typed() => KeryxTyped.Send(_keryx);

    [Benchmark]
    public ValueTask<int> Keryx_Generic() => KeryxGeneric.Send(_keryx);

    [Benchmark]
    public ValueTask<int> MediatorScoped_Typed() => MediatorScopedTyped.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<int> MediatorScoped_Generic() => MediatorScopedGeneric.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Typed() => MediatorSingletonTyped.Send(_mediatorSingleton);

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Generic() => MediatorSingletonGeneric.Send(_mediatorSingleton);

    [Benchmark]
    public Task<int> MediatR_Generic() => MediatR.Send(_mediatr);

    [Benchmark]
    public ValueTask<int> ImmediateHandlers_Typed() => _immediateHandler.HandleAsync(_immediate);

    [Benchmark]
    public ValueTask<int> ZeroAlloc_Static() => ZeroAlloc.Mediator.Mediator.Send(_zeroAlloc);

    [Benchmark]
    public ValueTask<int> ZeroAlloc_IMediator() => ZeroAllocMediator.Send(_zeroAlloc);
}

/// <summary><c>Send</c> to a synchronous handler through 3 pass-through behaviors.</summary>
[Expect(Result = 42, Hits = 4, BaselineHits = 1)]
public class SendSyncDepth3 : ComparisonBenchmark
{
    private readonly B.Ping3 _baseline = new(21);
    private readonly IH.Ping3 _immediate = new(21);
    private readonly K.Ping3 _keryx = new(21);
    private readonly MR.Ping3 _mediatr = new(21);
    private readonly MS.Ping3 _mediatorScoped = new(21);
    private readonly MSi.Ping3 _mediatorSingleton = new(21);
    private readonly ZA.Ping3 _zeroAlloc = new(21);
    private IH.Ping3Handler.Handler _immediateHandler = null!;

    private protected override void Resolve(Subjects subjects) => _immediateHandler = subjects.Immediate<IH.Ping3Handler.Handler>();

    [Benchmark(Baseline = true)]
    public ValueTask<int> Baseline() => B.Handlers.Handle(_baseline);

    [Benchmark]
    public ValueTask<int> Keryx_Typed() => KeryxTyped.Send(_keryx);

    [Benchmark]
    public ValueTask<int> Keryx_Generic() => KeryxGeneric.Send(_keryx);

    [Benchmark]
    public ValueTask<int> MediatorScoped_Typed() => MediatorScopedTyped.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<int> MediatorScoped_Generic() => MediatorScopedGeneric.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Typed() => MediatorSingletonTyped.Send(_mediatorSingleton);

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Generic() => MediatorSingletonGeneric.Send(_mediatorSingleton);

    [Benchmark]
    public Task<int> MediatR_Generic() => MediatR.Send(_mediatr);

    [Benchmark]
    public ValueTask<int> ImmediateHandlers_Typed() => _immediateHandler.HandleAsync(_immediate);

    [Benchmark]
    public ValueTask<int> ZeroAlloc_Static() => ZeroAlloc.Mediator.Mediator.Send(_zeroAlloc);

    [Benchmark]
    public ValueTask<int> ZeroAlloc_IMediator() => ZeroAllocMediator.Send(_zeroAlloc);
}

/// <summary><c>Send</c> to a synchronous handler through 8 pass-through behaviors.</summary>
[Expect(Result = 42, Hits = 9, BaselineHits = 1)]
public class SendSyncDepth8 : ComparisonBenchmark
{
    private readonly B.Ping8 _baseline = new(21);
    private readonly IH.Ping8 _immediate = new(21);
    private readonly K.Ping8 _keryx = new(21);
    private readonly MR.Ping8 _mediatr = new(21);
    private readonly MS.Ping8 _mediatorScoped = new(21);
    private readonly MSi.Ping8 _mediatorSingleton = new(21);
    private readonly ZA.Ping8 _zeroAlloc = new(21);
    private IH.Ping8Handler.Handler _immediateHandler = null!;

    private protected override void Resolve(Subjects subjects) => _immediateHandler = subjects.Immediate<IH.Ping8Handler.Handler>();

    [Benchmark(Baseline = true)]
    public ValueTask<int> Baseline() => B.Handlers.Handle(_baseline);

    [Benchmark]
    public ValueTask<int> Keryx_Typed() => KeryxTyped.Send(_keryx);

    [Benchmark]
    public ValueTask<int> Keryx_Generic() => KeryxGeneric.Send(_keryx);

    [Benchmark]
    public ValueTask<int> MediatorScoped_Typed() => MediatorScopedTyped.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<int> MediatorScoped_Generic() => MediatorScopedGeneric.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Typed() => MediatorSingletonTyped.Send(_mediatorSingleton);

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Generic() => MediatorSingletonGeneric.Send(_mediatorSingleton);

    [Benchmark]
    public Task<int> MediatR_Generic() => MediatR.Send(_mediatr);

    [Benchmark]
    public ValueTask<int> ImmediateHandlers_Typed() => _immediateHandler.HandleAsync(_immediate);

    [Benchmark]
    public ValueTask<int> ZeroAlloc_Static() => ZeroAlloc.Mediator.Mediator.Send(_zeroAlloc);

    [Benchmark]
    public ValueTask<int> ZeroAlloc_IMediator() => ZeroAllocMediator.Send(_zeroAlloc);
}

/// <summary><c>Send</c> to a handler that awaits <c>Task.Yield()</c> through 0 pass-through behaviors.</summary>
[Expect(Result = 42, Hits = 1, BaselineHits = 1)]
public class SendYieldDepth0 : ComparisonBenchmark
{
    private readonly B.YieldPing0 _baseline = new(21);
    private readonly IH.YieldPing0 _immediate = new(21);
    private readonly K.YieldPing0 _keryx = new(21);
    private readonly MR.YieldPing0 _mediatr = new(21);
    private readonly MS.YieldPing0 _mediatorScoped = new(21);
    private readonly MSi.YieldPing0 _mediatorSingleton = new(21);
    private readonly ZA.YieldPing0 _zeroAlloc = new(21);
    private IH.YieldPing0Handler.Handler _immediateHandler = null!;

    private protected override void Resolve(Subjects subjects) => _immediateHandler = subjects.Immediate<IH.YieldPing0Handler.Handler>();

    [Benchmark(Baseline = true)]
    public ValueTask<int> Baseline() => B.Handlers.Handle(_baseline);

    [Benchmark]
    public ValueTask<int> Keryx_Typed() => KeryxTyped.Send(_keryx);

    [Benchmark]
    public ValueTask<int> Keryx_Generic() => KeryxGeneric.Send(_keryx);

    [Benchmark]
    public ValueTask<int> MediatorScoped_Typed() => MediatorScopedTyped.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<int> MediatorScoped_Generic() => MediatorScopedGeneric.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Typed() => MediatorSingletonTyped.Send(_mediatorSingleton);

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Generic() => MediatorSingletonGeneric.Send(_mediatorSingleton);

    [Benchmark]
    public Task<int> MediatR_Generic() => MediatR.Send(_mediatr);

    [Benchmark]
    public ValueTask<int> ImmediateHandlers_Typed() => _immediateHandler.HandleAsync(_immediate);

    [Benchmark]
    public ValueTask<int> ZeroAlloc_Static() => ZeroAlloc.Mediator.Mediator.Send(_zeroAlloc);

    [Benchmark]
    public ValueTask<int> ZeroAlloc_IMediator() => ZeroAllocMediator.Send(_zeroAlloc);
}

/// <summary><c>Send</c> to a handler that awaits <c>Task.Yield()</c> through 1 pass-through behavior.</summary>
[Expect(Result = 42, Hits = 2, BaselineHits = 1)]
public class SendYieldDepth1 : ComparisonBenchmark
{
    private readonly B.YieldPing1 _baseline = new(21);
    private readonly IH.YieldPing1 _immediate = new(21);
    private readonly K.YieldPing1 _keryx = new(21);
    private readonly MR.YieldPing1 _mediatr = new(21);
    private readonly MS.YieldPing1 _mediatorScoped = new(21);
    private readonly MSi.YieldPing1 _mediatorSingleton = new(21);
    private readonly ZA.YieldPing1 _zeroAlloc = new(21);
    private IH.YieldPing1Handler.Handler _immediateHandler = null!;

    private protected override void Resolve(Subjects subjects) => _immediateHandler = subjects.Immediate<IH.YieldPing1Handler.Handler>();

    [Benchmark(Baseline = true)]
    public ValueTask<int> Baseline() => B.Handlers.Handle(_baseline);

    [Benchmark]
    public ValueTask<int> Keryx_Typed() => KeryxTyped.Send(_keryx);

    [Benchmark]
    public ValueTask<int> Keryx_Generic() => KeryxGeneric.Send(_keryx);

    [Benchmark]
    public ValueTask<int> MediatorScoped_Typed() => MediatorScopedTyped.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<int> MediatorScoped_Generic() => MediatorScopedGeneric.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Typed() => MediatorSingletonTyped.Send(_mediatorSingleton);

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Generic() => MediatorSingletonGeneric.Send(_mediatorSingleton);

    [Benchmark]
    public Task<int> MediatR_Generic() => MediatR.Send(_mediatr);

    [Benchmark]
    public ValueTask<int> ImmediateHandlers_Typed() => _immediateHandler.HandleAsync(_immediate);

    [Benchmark]
    public ValueTask<int> ZeroAlloc_Static() => ZeroAlloc.Mediator.Mediator.Send(_zeroAlloc);

    [Benchmark]
    public ValueTask<int> ZeroAlloc_IMediator() => ZeroAllocMediator.Send(_zeroAlloc);
}

/// <summary><c>Send</c> to a handler that awaits <c>Task.Yield()</c> through 3 pass-through behaviors.</summary>
[Expect(Result = 42, Hits = 4, BaselineHits = 1)]
public class SendYieldDepth3 : ComparisonBenchmark
{
    private readonly B.YieldPing3 _baseline = new(21);
    private readonly IH.YieldPing3 _immediate = new(21);
    private readonly K.YieldPing3 _keryx = new(21);
    private readonly MR.YieldPing3 _mediatr = new(21);
    private readonly MS.YieldPing3 _mediatorScoped = new(21);
    private readonly MSi.YieldPing3 _mediatorSingleton = new(21);
    private readonly ZA.YieldPing3 _zeroAlloc = new(21);
    private IH.YieldPing3Handler.Handler _immediateHandler = null!;

    private protected override void Resolve(Subjects subjects) => _immediateHandler = subjects.Immediate<IH.YieldPing3Handler.Handler>();

    [Benchmark(Baseline = true)]
    public ValueTask<int> Baseline() => B.Handlers.Handle(_baseline);

    [Benchmark]
    public ValueTask<int> Keryx_Typed() => KeryxTyped.Send(_keryx);

    [Benchmark]
    public ValueTask<int> Keryx_Generic() => KeryxGeneric.Send(_keryx);

    [Benchmark]
    public ValueTask<int> MediatorScoped_Typed() => MediatorScopedTyped.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<int> MediatorScoped_Generic() => MediatorScopedGeneric.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Typed() => MediatorSingletonTyped.Send(_mediatorSingleton);

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Generic() => MediatorSingletonGeneric.Send(_mediatorSingleton);

    [Benchmark]
    public Task<int> MediatR_Generic() => MediatR.Send(_mediatr);

    [Benchmark]
    public ValueTask<int> ImmediateHandlers_Typed() => _immediateHandler.HandleAsync(_immediate);

    [Benchmark]
    public ValueTask<int> ZeroAlloc_Static() => ZeroAlloc.Mediator.Mediator.Send(_zeroAlloc);

    [Benchmark]
    public ValueTask<int> ZeroAlloc_IMediator() => ZeroAllocMediator.Send(_zeroAlloc);
}

/// <summary><c>Send</c> to a handler that awaits <c>Task.Yield()</c> through 8 pass-through behaviors.</summary>
[Expect(Result = 42, Hits = 9, BaselineHits = 1)]
public class SendYieldDepth8 : ComparisonBenchmark
{
    private readonly B.YieldPing8 _baseline = new(21);
    private readonly IH.YieldPing8 _immediate = new(21);
    private readonly K.YieldPing8 _keryx = new(21);
    private readonly MR.YieldPing8 _mediatr = new(21);
    private readonly MS.YieldPing8 _mediatorScoped = new(21);
    private readonly MSi.YieldPing8 _mediatorSingleton = new(21);
    private readonly ZA.YieldPing8 _zeroAlloc = new(21);
    private IH.YieldPing8Handler.Handler _immediateHandler = null!;

    private protected override void Resolve(Subjects subjects) => _immediateHandler = subjects.Immediate<IH.YieldPing8Handler.Handler>();

    [Benchmark(Baseline = true)]
    public ValueTask<int> Baseline() => B.Handlers.Handle(_baseline);

    [Benchmark]
    public ValueTask<int> Keryx_Typed() => KeryxTyped.Send(_keryx);

    [Benchmark]
    public ValueTask<int> Keryx_Generic() => KeryxGeneric.Send(_keryx);

    [Benchmark]
    public ValueTask<int> MediatorScoped_Typed() => MediatorScopedTyped.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<int> MediatorScoped_Generic() => MediatorScopedGeneric.Send(_mediatorScoped);

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Typed() => MediatorSingletonTyped.Send(_mediatorSingleton);

    [Benchmark]
    public ValueTask<int> MediatorSingleton_Generic() => MediatorSingletonGeneric.Send(_mediatorSingleton);

    [Benchmark]
    public Task<int> MediatR_Generic() => MediatR.Send(_mediatr);

    [Benchmark]
    public ValueTask<int> ImmediateHandlers_Typed() => _immediateHandler.HandleAsync(_immediate);

    [Benchmark]
    public ValueTask<int> ZeroAlloc_Static() => ZeroAlloc.Mediator.Mediator.Send(_zeroAlloc);

    [Benchmark]
    public ValueTask<int> ZeroAlloc_IMediator() => ZeroAllocMediator.Send(_zeroAlloc);
}
