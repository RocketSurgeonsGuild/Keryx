using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Keryx.Benchmarks.Common;
using K = Keryx.Benchmarks.KeryxHost;
using MS = Keryx.Benchmarks.MediatorScoped;
using MSi = Keryx.Benchmarks.MediatorSingleton;

namespace Keryx.Benchmarks.Comparison;

/// <summary>
///     The base of every comparison benchmark (ADR 0001, Benchmarks): builds each library's container once per benchmark process
///     (<see cref="Subjects" />) and copies the mediators into fields, so each benchmark method is one call on a field. Each derived class is
///     one scenario; its <see cref="ExpectAttribute" /> says what <c>--verify</c> checks.
/// </summary>
/// <remarks>
///     Grouped by job as well as category, so a run with several runtimes never takes one runtime's rows as ratios of another runtime's
///     <c>Baseline</c>. BenchmarkDotNet 0.15.8 marks the first of several <c>--runtimes</c> as the baseline job and then counts as the
///     baseline only the case that is both the baseline method and in that job, so the other runtimes' ratio columns show <c>?</c>: run
///     <c>--runtimes nativeaot10.0</c> on its own for NativeAOT ratios.
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory, BenchmarkLogicalGroupRule.ByJob)]
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

    /// <summary>
    ///     Fails unless this build has no verification probe (owner decision 34, open-questions OQ-73): the runner sets
    ///     <c>KeryxBenchmarksProbe=false</c> before BenchmarkDotNet builds, and a build that missed it (a BenchmarkDotNet or MSBuild change,
    ///     or a toolchain that reuses the runner's own probe-on binaries, as <c>--inProcess</c> does) would measure the probe in every
    ///     handler and behavior call. Then builds the subjects.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        if (!Probe.IsCompiledOut())
        {
            throw new InvalidOperationException(
                "This benchmark build carries the verification probe; KeryxBenchmarksProbe=false did not reach BenchmarkDotNet's build."
            );
        }

        CreateSubjects();
    }

    /// <summary>
    ///     Builds each library's container and copies the mediators into fields; <c>--verify</c>, which runs the probe-on build, calls it
    ///     in place of <see cref="Setup" />.
    /// </summary>
    internal void CreateSubjects()
    {
        _subjects = new(FanOut);
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

    /// <summary>Whether this scenario publishes to the fan-out projects (<see cref="FanOutBenchmark" />), whose containers only it builds.</summary>
    private protected virtual bool FanOut => false;

    /// <summary>Resolves what only this scenario needs (Immediate.Handlers' generated handler classes).</summary>
    private protected virtual void Resolve(Subjects subjects) { }
}
