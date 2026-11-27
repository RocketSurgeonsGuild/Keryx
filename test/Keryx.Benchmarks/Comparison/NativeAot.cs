using System.Runtime.CompilerServices;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Running;
using Keryx.Benchmarks.Prototype;

namespace Keryx.Benchmarks.Comparison;

/// <summary>
///     The comparison suite under NativeAOT (ADR 0001, Benchmarks; owner decision 29): the subjects that cannot run there, each with the
///     reason found by building and running them (open-questions, "NativeAOT benchmark run"). Under NativeAOT, <see cref="Subjects" /> does
///     not build their containers, <c>--verify</c> reports them as n/a instead of calling them, and <see cref="Filter" /> leaves their
///     benchmarks (and the JIT-only Phase 4 prototype) out of a BenchmarkDotNet run with the NativeAOT toolchain
///     (<c>--runtimes nativeaot10.0</c>). Every other subject runs every scenario.
/// </summary>
internal static class NativeAot
{
    /// <summary>
    ///     MediatR 15, verified with the .NET 10 ILCompiler: <c>AddMediatR</c> with the suite's behaviors throws at startup
    ///     (<c>AddOpenStreamBehavior</c>: "StreamPass1`2 must implement MediatR.IStreamPipelineBehavior`2"), and with no behaviors at all (the
    ///     parallel publish container) the validated container cannot construct MediatR's own types ("A suitable constructor for type
    ///     'MediatR.Mediator' could not be located"). The publish reports MediatR.dll's trim and AOT warnings (IL2104, IL3053).
    /// </summary>
    public const string MediatRReason =
        "MediatR 15 does not run under NativeAOT: AddMediatR throws at startup (AddOpenStreamBehavior: \"StreamPass1`2 must implement "
      + "MediatR.IStreamPipelineBehavior`2\"), and without behaviors the container cannot construct MediatR.Mediator; MediatR.dll produces "
      + "trim and AOT warnings (IL2104, IL3053).";

    /// <summary>Whether this process is a NativeAOT build (no dynamic code: the JIT is not there).</summary>
    public static bool IsActive => !RuntimeFeature.IsDynamicCodeSupported;

    /// <summary>
    ///     Leaves the n/a subjects' benchmarks and the Phase 4 prototype out of NativeAOT jobs; every JIT job keeps every benchmark. The
    ///     prototype is JIT-only: it is evidence about the JIT's code (its <c>DisassemblyDiagnoser</c>, which BenchmarkDotNet 0.15.8 rejects
    ///     for a NativeAOT job with a critical validation error, failing the whole run), so it has no meaning under NativeAOT. Config filters
    ///     run before validation, so the diagnoser's validator never sees a NativeAOT case.
    /// </summary>
    public static readonly IFilter Filter = new SimpleFilter(
        static benchmarkCase => benchmarkCase.Job.Environment.Runtime is not NativeAotRuntime
         || ( benchmarkCase.Descriptor.Type != typeof(PipelinePrototypeBenchmarks)
             && NotApplicable(benchmarkCase.Descriptor.WorkloadMethod.Name) is null )
    );

    /// <summary>
    ///     Whether <see cref="Subjects" /> skips <paramref name="library" />'s container: in a NativeAOT build, for a library n/a there.
    /// </summary>
    public static bool Skips(Library library) => IsActive && library is Library.MediatR or Library.MediatRParallel;

    /// <summary>
    ///     Why the benchmark method <paramref name="benchmark" /> (named for its subject: <c>MediatR_Generic</c>, <c>Keryx_Typed</c>, ...)
    ///     cannot run under NativeAOT, or <see langword="null" /> when it can.
    /// </summary>
    public static string? NotApplicable(string benchmark) =>
        benchmark.StartsWith("MediatR_", StringComparison.Ordinal) ? MediatRReason : null;
}
