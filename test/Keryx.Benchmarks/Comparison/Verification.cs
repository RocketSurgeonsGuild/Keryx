using System.Reflection;
using BenchmarkDotNet.Attributes;
using Keryx.Benchmarks.Common;

namespace Keryx.Benchmarks.Comparison;

/// <summary>
///     <c>Keryx.Benchmarks --verify</c>: calls every comparison benchmark twice (cold, then warm) with <see cref="Probe" /> on and checks its
///     result and the number of handler and behavior calls against the scenario's <see cref="ExpectAttribute" />, so a library that is not
///     configured to run the scenario's behaviors fails here instead of looking fast in the results.
/// </summary>
internal static class Verification
{
    public static async Task<int> RunAsync()
    {
        if (!Probe.Enabled)
        {
            throw new InvalidOperationException($"Set the {Probe.SwitchName} AppContext switch before verifying.");
        }

        var failures = new List<string>();
        var checkedCount = 0;
        var scenarios = typeof(ComparisonBenchmark)
                       .Assembly.GetTypes()
                       .Where(static type => type is { IsAbstract: false } && type.IsSubclassOf(typeof(ComparisonBenchmark)))
                       .OrderBy(static type => type.Name, StringComparer.Ordinal);
        foreach (var scenario in scenarios)
        {
            var expect = scenario.GetCustomAttribute<ExpectAttribute>()
             ?? throw new InvalidOperationException($"{scenario.Name} has no [Expect].");
            var benchmark = (ComparisonBenchmark)Activator.CreateInstance(scenario)!;
            benchmark.Setup();
            try
            {
                foreach (var method in scenario.GetMethods().Where(static method => method.IsDefined(typeof(BenchmarkAttribute))).OrderBy(static method => method.Name, StringComparer.Ordinal))
                {
                    var isBaseline = method.GetCustomAttribute<BenchmarkAttribute>()!.Baseline;
                    var hits = isBaseline && expect.BaselineHits >= 0 ? expect.BaselineHits : expect.Hits;
                    for (var run = 0; run < 2; run++)
                    {
                        _ = Probe.Take();
                        var result = await InvokeAsync(benchmark, method);
                        var actualHits = Probe.Take();
                        if (result != expect.Result || actualHits != hits)
                        {
                            failures.Add($"{scenario.Name}.{method.Name} (call {run + 1}): result {result}, expected {expect.Result}; calls {actualHits}, expected {hits}");
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

        foreach (var failure in failures)
        {
            Console.Error.WriteLine("FAILED: " + failure);
        }

        Console.WriteLine($"Verified {checkedCount} comparison benchmarks: {( failures.Count == 0 ? "all passed" : $"{failures.Count} failed" )}.");
        return failures.Count == 0 ? 0 : 1;
    }

    private static async ValueTask<int> InvokeAsync(ComparisonBenchmark benchmark, MethodInfo method) =>
        method.Invoke(benchmark, null) switch
        {
            ValueTask<int> value => await value,
            Task<int> task => await task,
            ValueTask<Unit> unit => await Done(unit),
            ValueTask value => await Done(value),
            Task task => await Done(new ValueTask(task)),
            var other => throw new InvalidOperationException($"{method.Name} returned {other?.GetType().Name ?? "null"}."),
        };

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
}
