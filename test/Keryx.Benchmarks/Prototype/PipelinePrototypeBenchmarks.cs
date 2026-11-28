using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using Keryx.Benchmarks.Host;

namespace Keryx.Benchmarks.Prototype;

/// <summary>
///     The Phase 4 gate (ADR 0001, Phase 4 gate): the hand-written struct pipeline (<see cref="PrototypeMediator" />) at depths 1, 3 and 8
///     against a hand-written nested-call baseline of the same depth (<see cref="NestedCallDepth1" />, <see cref="NestedCallDepth3" />,
///     <see cref="NestedCallDepth8" />, each its own fixed chain) doing the same work. It passes when every struct
///     pipeline allocates 0 B, the disassembly shows each behavior's <c>Handle</c> and each <c>next.InvokeAsync</c> called directly (no
///     generic virtual method lookup), and depth 3 is within 1.25x of the baseline. Results are recorded in docs/adr/open-questions.md.
/// </summary>
/// <remarks>
///     Run with <c>dotnet run -c Release -f net10.0 -- --filter *PipelinePrototype*</c>. BenchmarkDotNet's <c>DisassemblyDiagnoser</c>
///     supports only Windows and Linux, so <see cref="GateConfig" /> adds it there; elsewhere, <c>Program</c>'s <c>--prototype-disasm</c>
///     mode runs the same pipelines to Tier 1 under <c>DOTNET_JitDisasm</c> (see the remarks there).
/// </remarks>
[MemoryDiagnoser]
[Config(typeof(GateConfig))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory, BenchmarkLogicalGroupRule.ByJob)]
[CategoriesColumn]
public class PipelinePrototypeBenchmarks
{
    /// <summary>Adds the <c>DisassemblyDiagnoser</c> where BenchmarkDotNet supports it (Windows and Linux).</summary>
    public sealed class GateConfig : ManualConfig
    {
        public GateConfig()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                AddDiagnoser(new DisassemblyDiagnoser(new DisassemblyDiagnoserConfig(maxDepth: 3, printSource: true, exportGithubMarkdown: true)));
            }
        }
    }

    /// <summary>
    ///     Runs every prototype pipeline and the baselines often enough for the JIT to reach Tier 1 (with Dynamic PGO), so
    ///     <c>DOTNET_JitDisasm</c> prints their final code. The loop is never optimized and calls through delegates, so it does not inline the
    ///     benchmark methods (as an optimized or OSR loop would) and they tier up on their own, as under BenchmarkDotNet.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
    public static int RunForDisassembly()
    {
        var benchmarks = new PipelinePrototypeBenchmarks();
        Func<int>[] calls =
        [
            benchmarks.NestedCalls_Depth1,
            benchmarks.StructPipeline_Depth1,
            benchmarks.NestedCalls_Depth3,
            benchmarks.StructPipeline_Depth3,
            benchmarks.NestedCalls_Depth8,
            benchmarks.StructPipeline_Depth8,
        ];
        var sum = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(3))
        {
            for (var i = 0; i < 200; i++)
            {
                foreach (var call in calls)
                {
                    sum += call();
                }
            }

            Thread.Sleep(10);
        }

        return sum;
    }

    /// <summary>
    ///     Calls per benchmark invocation. One call of a depth-1 pipeline takes well under a nanosecond, below BenchmarkDotNet's resolution; a
    ///     loop of this many makes each invocation long enough to measure, and <c>OperationsPerInvoke</c> reports the time per call.
    /// </summary>
    private const int Operations = 1_000;

    private readonly Ping _request = new(21);
    private readonly PrototypeMediator _mediator = new();
    private readonly NestedCallDepth1 _depth1 = new();
    private readonly NestedCallDepth3 _depth3 = new();
    private readonly NestedCallDepth8 _depth8 = new();

    [Benchmark(Baseline = true, OperationsPerInvoke = Operations)]
    [BenchmarkCategory("Depth1")]
    public int NestedCalls_Depth1()
    {
        var sum = 0;
        for (var i = 0; i < Operations; i++)
        {
            sum += Completed.Result(_depth1.Send(_request, CancellationToken.None));
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = Operations)]
    [BenchmarkCategory("Depth1")]
    public int StructPipeline_Depth1()
    {
        var sum = 0;
        for (var i = 0; i < Operations; i++)
        {
            sum += Completed.Result(_mediator.SendDepth1(_request, CancellationToken.None));
        }

        return sum;
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = Operations)]
    [BenchmarkCategory("Depth3")]
    public int NestedCalls_Depth3()
    {
        var sum = 0;
        for (var i = 0; i < Operations; i++)
        {
            sum += Completed.Result(_depth3.Send(_request, CancellationToken.None));
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = Operations)]
    [BenchmarkCategory("Depth3")]
    public int StructPipeline_Depth3()
    {
        var sum = 0;
        for (var i = 0; i < Operations; i++)
        {
            sum += Completed.Result(_mediator.SendDepth3(_request, CancellationToken.None));
        }

        return sum;
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = Operations)]
    [BenchmarkCategory("Depth8")]
    public int NestedCalls_Depth8()
    {
        var sum = 0;
        for (var i = 0; i < Operations; i++)
        {
            sum += Completed.Result(_depth8.Send(_request, CancellationToken.None));
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = Operations)]
    [BenchmarkCategory("Depth8")]
    public int StructPipeline_Depth8()
    {
        var sum = 0;
        for (var i = 0; i < Operations; i++)
        {
            sum += Completed.Result(_mediator.SendDepth8(_request, CancellationToken.None));
        }

        return sum;
    }
}
