extern alias ZeroAllocFanOut;
using Microsoft.Extensions.DependencyInjection;
using K = Keryx.Benchmarks.FanOut.KeryxHost;
using MP = Keryx.Benchmarks.FanOut.MediatorParallel;
using MS = Keryx.Benchmarks.FanOut.MediatorScoped;
using MSi = Keryx.Benchmarks.FanOut.MediatorSingleton;

namespace Keryx.Benchmarks.Comparison;

/// <summary>
///     The base of the fan-out scenarios (owner decision 41; FanOutBenchmarks.cs, written by test/benchmark-subjects/GenerateFanOut.cs): the
///     notifications with 25, 50 and 100 handlers live in each library's fan-out project (test/benchmark-subjects/Keryx.Benchmarks.FanOut.*),
///     so they change nothing in the projects the other scenarios measure (for Keryx, its scope state and dispatch: open-questions OQ-78).
///     <see cref="Subjects" /> builds those projects' containers only for these scenarios, and this class copies their mediators and scope
///     factories into fields, as <see cref="ComparisonBenchmark" /> does for the other subjects.
/// </summary>
public abstract class FanOutBenchmark : ComparisonBenchmark
{
    private protected K.KeryxMediator FanOutKeryxTyped = null!;
    private protected IMediator FanOutKeryxGeneric = null!;
    private protected MS.Mediator FanOutMediatorScopedTyped = null!;
    private protected Mediator.IMediator FanOutMediatorScopedGeneric = null!;
    private protected MSi.Mediator FanOutMediatorSingletonTyped = null!;
    private protected Mediator.IMediator FanOutMediatorSingletonGeneric = null!;
    private protected MP.Mediator FanOutMediatorParallelTyped = null!;
    private protected Mediator.IMediator FanOutMediatorParallelGeneric = null!;
    private protected MediatR.IMediator FanOutMediatR = null!;
    private protected MediatR.IMediator FanOutMediatRParallel = null!;
    private protected ZeroAllocFanOut::ZeroAlloc.Mediator.IMediator FanOutZeroAlloc = null!;
    private protected FanOutScopeFactories FanOutScopes = null!;

    private protected sealed override bool FanOut => true;

    private protected sealed override void Resolve(Subjects subjects)
    {
        var fanOut = subjects.FanOut!;
        FanOutKeryxTyped = fanOut.KeryxTyped;
        FanOutKeryxGeneric = fanOut.KeryxGeneric;
        FanOutMediatorScopedTyped = fanOut.MediatorScopedTyped;
        FanOutMediatorScopedGeneric = fanOut.MediatorScopedGeneric;
        FanOutMediatorSingletonTyped = fanOut.MediatorSingletonTyped;
        FanOutMediatorSingletonGeneric = fanOut.MediatorSingletonGeneric;
        FanOutMediatorParallelTyped = fanOut.MediatorParallelTyped;
        FanOutMediatorParallelGeneric = fanOut.MediatorParallelGeneric;
        FanOutMediatR = fanOut.MediatR;
        FanOutMediatRParallel = fanOut.MediatRParallel;
        FanOutZeroAlloc = fanOut.ZeroAlloc;
        FanOutScopes = new(subjects);
        ResolveFanOut(subjects);
    }

    /// <summary>Resolves what only this scenario needs (the baseline's publisher, ZeroAlloc.Mediator's static factories).</summary>
    private protected virtual void ResolveFanOut(Subjects subjects) { }
}

/// <summary>The fan-out containers' scope factories, read once in setup, for the scope-per-request fan-out scenarios.</summary>
internal sealed class FanOutScopeFactories(Subjects subjects)
{
    public readonly IServiceScopeFactory Baseline = subjects.Scopes(Library.BaselineFanOut);
    public readonly IServiceScopeFactory Keryx = subjects.Scopes(Library.KeryxFanOut);
    public readonly IServiceScopeFactory MediatorScoped = subjects.Scopes(Library.MediatorScopedFanOut);
    public readonly IServiceScopeFactory MediatR = subjects.Scopes(Library.MediatRFanOut);
    public readonly IServiceScopeFactory ZeroAlloc = subjects.Scopes(Library.ZeroAllocFanOut);
}
