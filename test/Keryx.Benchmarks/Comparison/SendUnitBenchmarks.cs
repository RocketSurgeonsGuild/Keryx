// Send of an IRequest (no response) whose handler returns non-generic ValueTask / Task and awaits Task.Yield(): the one async path Keryx
// generates, UnitAdapter.ToUnit (ADR 0001, Performance contract, "Send, yielding handler": it must add 0 bytes in steady state; this is the
// benchmark that checks it). Keryx only: the other libraries adapt void-like handlers their own way, which is not what this checks. The
// baseline awaits the same handler body directly, so its allocated column is the handler's own state machine and nothing else.
using BenchmarkDotNet.Attributes;
using B = Keryx.Benchmarks.Baseline;
using K = Keryx.Benchmarks.KeryxHost;

namespace Keryx.Benchmarks.Comparison;

/// <summary><c>Send</c> of an <c>IRequest</c> whose handler is <c>async ValueTask</c> and awaits <c>Task.Yield()</c>.</summary>
[Expect(Hits = 1)]
public class SendUnitYieldValueTask : ComparisonBenchmark
{
    private readonly B.YieldUnitValueTask _baseline = new(21);
    private readonly K.YieldUnitValueTask _keryx = new(21);

    [Benchmark(Baseline = true)]
    public ValueTask Baseline() => B.Handlers.Handle(_baseline);

    [Benchmark]
    public ValueTask<Unit> Keryx_Typed() => KeryxTyped.Send(_keryx);

    [Benchmark]
    public ValueTask<Unit> Keryx_Generic() => KeryxGeneric.Send(_keryx);
}

/// <summary><c>Send</c> of an <c>IRequest</c> whose handler is <c>async Task</c> and awaits <c>Task.Yield()</c>.</summary>
[Expect(Hits = 1)]
public class SendUnitYieldTask : ComparisonBenchmark
{
    private readonly B.YieldUnitTask _baseline = new(21);
    private readonly K.YieldUnitTask _keryx = new(21);

    [Benchmark(Baseline = true)]
    public Task Baseline() => B.Handlers.Handle(_baseline);

    [Benchmark]
    public ValueTask<Unit> Keryx_Typed() => KeryxTyped.Send(_keryx);

    [Benchmark]
    public ValueTask<Unit> Keryx_Generic() => KeryxGeneric.Send(_keryx);
}
