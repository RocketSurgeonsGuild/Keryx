using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using K = Keryx.Benchmarks.KeryxHost;
using MS = Keryx.Benchmarks.MediatorScoped;
using MSi = Keryx.Benchmarks.MediatorSingleton;

namespace Keryx.Benchmarks.Comparison;

/// <summary>
///     The base of every comparison benchmark (ADR 0001, Benchmarks): builds each library's container once per benchmark process
///     (<see cref="Subjects" />) and copies the mediators into fields, so each benchmark method is one call on a field. Each derived class is
///     one scenario; its <see cref="ExpectAttribute" /> says what <c>--verify</c> checks.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public abstract class ComparisonBenchmark
{
    private Subjects? _subjects;

    // Fields rather than properties: a benchmark reads one field, as a caller holding the mediator would.
    private protected K.KeryxMediator KeryxTyped = null!;
    private protected IMediator KeryxGeneric = null!;
    private protected MS.Mediator MediatorScopedTyped = null!;
    private protected Mediator.IMediator MediatorScopedGeneric = null!;
    private protected MSi.Mediator MediatorSingletonTyped = null!;
    private protected Mediator.IMediator MediatorSingletonGeneric = null!;
    private protected MediatR.IMediator MediatR = null!;
    private protected ZeroAlloc.Mediator.IMediator ZeroAllocMediator = null!;

    [GlobalSetup]
    public void Setup()
    {
        _subjects = new();
        KeryxTyped = _subjects.KeryxTyped;
        KeryxGeneric = _subjects.KeryxGeneric;
        MediatorScopedTyped = _subjects.MediatorScopedTyped;
        MediatorScopedGeneric = _subjects.MediatorScopedGeneric;
        MediatorSingletonTyped = _subjects.MediatorSingletonTyped;
        MediatorSingletonGeneric = _subjects.MediatorSingletonGeneric;
        MediatR = _subjects.MediatR;
        ZeroAllocMediator = _subjects.ZeroAllocMediator;
        Resolve(_subjects);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _subjects?.Dispose();
        _subjects = null;
    }

    /// <summary>Resolves what only this scenario needs (Immediate.Handlers' generated handler classes).</summary>
    private protected virtual void Resolve(Subjects subjects) { }
}
