using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using Keryx.Benchmarks.Common;

namespace Keryx.Benchmarks.Comparison;

/// <summary>
///     <c>Keryx.Benchmarks --verify</c>: calls every comparison benchmark twice (cold, then warm) with <see cref="Probe" /> on and checks its
///     result (a real-world scenario's <see cref="Pong" />: id and name) and the number of probe hits (handler and behavior calls; in the
///     real-world scenarios also validator calls, repository lookups, SQLite queries and formatted log messages) against the scenario's
///     <see cref="ExpectAttribute" />, so a library that is not configured to run the scenario's behaviors fails here instead of looking fast
///     in the results. The same check runs in the NativeAOT build (this project published with NativeAOT and run with <c>--verify</c>), where a
///     subject that is n/a under NativeAOT (<see cref="NativeAot" />) is reported and skipped instead of called.
/// </summary>
internal static class Verification
{
    /// <summary>
    ///     Every comparison scenario, listed rather than found by scanning the assembly so the NativeAOT build can call them without trim
    ///     warnings. <see cref="ScenarioListValidator" /> fails a benchmark run that finds a <see cref="ComparisonBenchmark" /> missing here.
    /// </summary>
    public static readonly IReadOnlyList<Scenario> Scenarios =
    [
        Scenario.Of<PublishTo1>(),
        Scenario.Of<PublishTo100>(),
        Scenario.Of<PublishTo25>(),
        Scenario.Of<PublishTo5>(),
        Scenario.Of<PublishTo50>(),
        Scenario.Of<RealWorldBehaviors>(),
        Scenario.Of<RealWorldBehaviorsScopePerRequest>(),
        Scenario.Of<RealWorldPing>(),
        Scenario.Of<RealWorldPingScopePerRequest>(),
        Scenario.Of<RealWorldPublish100ScopePerRequest>(),
        Scenario.Of<RealWorldPublish1ScopePerRequest>(),
        Scenario.Of<RealWorldPublish25ScopePerRequest>(),
        Scenario.Of<RealWorldPublish50ScopePerRequest>(),
        Scenario.Of<RealWorldPublish5ScopePerRequest>(),
        Scenario.Of<RealWorldPublishAsync1>(),
        Scenario.Of<RealWorldPublishAsync100>(),
        Scenario.Of<RealWorldPublishAsync25>(),
        Scenario.Of<RealWorldPublishAsync5>(),
        Scenario.Of<RealWorldPublishAsync50>(),
        Scenario.Of<RealWorldPublishBehavior100ScopePerRequest>(),
        Scenario.Of<RealWorldPublishBehavior1ScopePerRequest>(),
        Scenario.Of<RealWorldPublishBehavior25ScopePerRequest>(),
        Scenario.Of<RealWorldPublishBehavior50ScopePerRequest>(),
        Scenario.Of<RealWorldPublishBehavior5ScopePerRequest>(),
        Scenario.Of<RealWorldPublishParallel1>(),
        Scenario.Of<RealWorldPublishParallel100>(),
        Scenario.Of<RealWorldPublishParallel25>(),
        Scenario.Of<RealWorldPublishParallel5>(),
        Scenario.Of<RealWorldPublishParallel50>(),
        Scenario.Of<RealWorldSqlite>(),
        Scenario.Of<SendSyncDepth0>(),
        Scenario.Of<SendSyncDepth1>(),
        Scenario.Of<SendSyncDepth3>(),
        Scenario.Of<SendSyncDepth8>(),
        Scenario.Of<SendUnitYieldTask>(),
        Scenario.Of<SendUnitYieldValueTask>(),
        Scenario.Of<SendYieldDepth0>(),
        Scenario.Of<SendYieldDepth1>(),
        Scenario.Of<SendYieldDepth3>(),
        Scenario.Of<SendYieldDepth8>(),
        Scenario.Of<StreamDepth0>(),
        Scenario.Of<StreamDepth3>(),
    ];

    public static async Task<int> RunAsync()
    {
        Probe.EnsureCounting();

        var runtime = NativeAot.IsActive ? "NativeAOT" : "JIT";
        var failures = new List<string>();
        var notApplicable = new List<(string Benchmark, string Scenario, string Reason)>();
        var checkedCount = 0;
        foreach (var scenario in Scenarios.OrderBy(static scenario => scenario.Type.Name, StringComparer.Ordinal))
        {
            var expect = scenario.Type.GetCustomAttribute<ExpectAttribute>()
             ?? throw new InvalidOperationException($"{scenario.Type.Name} has no [Expect].");
            var benchmark = scenario.Create();
            benchmark.CreateSubjects();
            try
            {
                foreach (var method in scenario.Benchmarks)
                {
                    if (NativeAot.IsActive && NativeAot.NotApplicable(method.Name) is { } reason)
                    {
                        notApplicable.Add((method.Name, scenario.Type.Name, reason));
                        continue;
                    }

                    var isBaseline = method.GetCustomAttribute<BenchmarkAttribute>()!.Baseline;
                    var hits = isBaseline && expect.BaselineHits >= 0 ? expect.BaselineHits : expect.Hits;
                    for (var run = 0; run < 2; run++)
                    {
                        _ = Probe.Take();
                        try
                        {
                            var (result, name) = await InvokeAsync(benchmark, method);
                            var actualHits = Probe.Take();
                            if (result != expect.Result || name != expect.Name || actualHits != hits)
                            {
                                failures.Add(
                                    $"{scenario.Type.Name}.{method.Name} (call {run + 1}): result {result} {name}, expected {expect.Result} {expect.Name}; hits {actualHits}, expected {hits}"
                                );
                            }
                        }
                        catch (Exception exception)
                        {
                            var thrown = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
                            failures.Add($"{scenario.Type.Name}.{method.Name} (call {run + 1}): threw {thrown}");
                            break;
                        }
                    }

                    checkedCount++;
                }
            }
            finally
            {
                benchmark.Cleanup();
            }
        }

        foreach (var subject in notApplicable.GroupBy(static skipped => (skipped.Benchmark, skipped.Reason)))
        {
            Console.WriteLine($"n/a under NativeAOT: {subject.Key.Benchmark} in {subject.Count()} scenarios. {subject.Key.Reason}");
        }

        foreach (var failure in failures)
        {
            Console.Error.WriteLine("FAILED: " + failure);
        }

        var skipped = notApplicable.Count == 0 ? "" : $", {notApplicable.Count} n/a";
        var outcome = failures.Count == 0 ? "all passed" : $"{failures.Count} failed";
        Console.WriteLine($"Verified {checkedCount} comparison benchmarks ({runtime}{skipped}): {outcome}.");
        return failures.Count == 0 ? 0 : 1;
    }

    private static async ValueTask<(int Result, string? Name)> InvokeAsync(ComparisonBenchmark benchmark, MethodInfo method) =>
        method.Invoke(benchmark, null) switch
        {
            ValueTask<int> value => (await value, null),
            Task<int> task => (await task, null),
            ValueTask<Pong> pong => Read(await pong),
            Task<Pong> pong => Read(await pong),
            ValueTask<Unit> unit => (await Done(unit), null),
            ValueTask value => (await Done(value), null),
            Task task => (await Done(new ValueTask(task)), null),
            var other => throw new InvalidOperationException($"{method.Name} returned {other?.GetType().Name ?? "null"}."),
        };

    private static (int Result, string? Name) Read(Pong pong) => (pong.Id, pong.Name);

    private static async ValueTask<int> Done(ValueTask task)
    {
        await task;
        return -1;
    }

    private static async ValueTask<int> Done(ValueTask<Unit> task)
    {
        _ = await task;
        return -1;
    }

    /// <summary>A comparison scenario: its type, how to create it, and its <c>[Benchmark]</c> methods in name order.</summary>
    internal sealed class Scenario
    {
        private readonly Func<ComparisonBenchmark> _create;

        private Scenario(Type type, Func<ComparisonBenchmark> create, IReadOnlyList<MethodInfo> benchmarks)
        {
            Type = type;
            _create = create;
            Benchmarks = benchmarks;
        }

        public Type Type { get; }
        public IReadOnlyList<MethodInfo> Benchmarks { get; }

        public ComparisonBenchmark Create() => _create();

        /// <summary>
        ///     The annotation keeps <typeparamref name="T" />'s public methods (the benchmarks) for reflection in a trimmed or NativeAOT build.
        /// </summary>
        public static Scenario Of<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] T>()
            where T : ComparisonBenchmark, new() =>
            new(
                typeof(T),
                static () => new T(),
                [
                    .. typeof(T)
                      .GetMethods()
                      .Where(static method => method.IsDefined(typeof(BenchmarkAttribute)))
                      .OrderBy(static method => method.Name, StringComparer.Ordinal),
                ]
            );
    }
}
