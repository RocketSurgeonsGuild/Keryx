using System.Collections.Immutable;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Keryx.Benchmarks.Comparison;

/// <summary>
///     Gives the NativeAOT job of <see cref="PublishTo1.Baseline" /> a fixed invocation count (owner decision 39, open-questions OQ-77), so a
///     full NativeAOT run measures every benchmark with no manual step. That benchmark is a call to a handler that returns <c>default</c>:
///     BenchmarkDotNet 0.15.8's pilot stage measures its first 16 calls as 0 ns and throws an <see cref="OverflowException" />, and a fixed
///     invocation count skips the pilot. Every other benchmark and job keeps its settings: a JIT job (whose pilot measures it), a job that is
///     not <see cref="RunStrategy.Throughput" /> (the Dry job runs one invocation and no pilot) and a job that already sets an invocation
///     count (<c>--invocationCount</c>) are left as they are.
/// </summary>
/// <remarks>
///     BenchmarkDotNet has no per-case job hook, but every benchmark case of a type passes through the config's orderer
///     (<see cref="IOrderer.GetExecutionOrder" />) before it is built and run, so this orderer replaces that one case there. The replacement's
///     job differs from the class's other cases in the invocation count, which would put it in a logical group of its own (the default
///     logical group key includes the job), leaving the class's other rows without a baseline. So every key and every summary order is
///     computed from the original cases, through <see cref="DefaultOrderer" />, and mapped back: the row sits in its class's group, in its
///     declared place, and is the baseline its ratios are computed against.
/// </remarks>
internal sealed class FixedInvocationCount : IOrderer
{
    /// <summary>
    ///     Calls per iteration, about what decision 32's pilot chose for the benchmark when it could still measure it (158,640,064 calls
    ///     per 200 ms iteration, with the verification probe in the call); a multiple of the default unroll factor, 16.
    /// </summary>
    public const long PublishTo1BaselineInvocationCount = 160_000_000;

    public static readonly FixedInvocationCount Instance = new();

    private readonly IOrderer _inner = DefaultOrderer.Instance;
    private readonly Dictionary<BenchmarkCase, BenchmarkCase> _originals = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BenchmarkCase, BenchmarkCase> _replacements = new(ReferenceEqualityComparer.Instance);
    private readonly Lock _lock = new();

    private FixedInvocationCount() { }

    public bool SeparateLogicalGroups => _inner.SeparateLogicalGroups;

    /// <summary>Whether <paramref name="benchmarkCase" /> gets <see cref="PublishTo1BaselineInvocationCount" />.</summary>
    public static bool Applies(BenchmarkCase benchmarkCase) =>
        benchmarkCase.Descriptor.Type == typeof(PublishTo1)
     && benchmarkCase.Descriptor.WorkloadMethod.Name == nameof(PublishTo1.Baseline)
     && benchmarkCase.Job.Environment.Runtime is NativeAotRuntime
     && benchmarkCase.Job.Run.RunStrategy == RunStrategy.Throughput
     && !benchmarkCase.Job.HasValue(RunMode.InvocationCountCharacteristic);

    public IEnumerable<BenchmarkCase> GetExecutionOrder(
        ImmutableArray<BenchmarkCase> benchmarksCase,
        IEnumerable<BenchmarkLogicalGroupRule>? order = null
    ) => _inner.GetExecutionOrder(benchmarksCase, order).Select(Replace).ToArray();

    public IEnumerable<BenchmarkCase> GetSummaryOrder(ImmutableArray<BenchmarkCase> benchmarksCases, Summary summary) =>
        _inner.GetSummaryOrder(Originals(benchmarksCases), summary).Select(Replacement).ToArray();

    public string? GetHighlightGroupKey(BenchmarkCase benchmarkCase) => _inner.GetHighlightGroupKey(Original(benchmarkCase));

    public string? GetLogicalGroupKey(ImmutableArray<BenchmarkCase> allBenchmarksCases, BenchmarkCase benchmarkCase) =>
        _inner.GetLogicalGroupKey(Originals(allBenchmarksCases), Original(benchmarkCase));

    public IEnumerable<IGrouping<string, BenchmarkCase>> GetLogicalGroupOrder(
        IEnumerable<IGrouping<string, BenchmarkCase>> logicalGroups,
        IEnumerable<BenchmarkLogicalGroupRule>? order = null
    ) => _inner.GetLogicalGroupOrder(logicalGroups, order);

    private BenchmarkCase Replace(BenchmarkCase benchmarkCase)
    {
        if (!Applies(benchmarkCase)) return benchmarkCase;
        lock (_lock)
        {
            if (_replacements.TryGetValue(benchmarkCase, out var existing)) return existing;
            var job = benchmarkCase.Job.WithInvocationCount(PublishTo1BaselineInvocationCount).Freeze();
            var replacement = BenchmarkCase.Create(benchmarkCase.Descriptor, job, benchmarkCase.Parameters, benchmarkCase.Config);
            _replacements.Add(benchmarkCase, replacement);
            _originals.Add(replacement, benchmarkCase);
            return replacement;
        }
    }

    private BenchmarkCase Original(BenchmarkCase benchmarkCase)
    {
        lock (_lock) return _originals.GetValueOrDefault(benchmarkCase, benchmarkCase);
    }

    private BenchmarkCase Replacement(BenchmarkCase benchmarkCase)
    {
        lock (_lock) return _replacements.GetValueOrDefault(benchmarkCase, benchmarkCase);
    }

    private ImmutableArray<BenchmarkCase> Originals(ImmutableArray<BenchmarkCase> benchmarkCases) => [.. benchmarkCases.Select(Original)];
}
