using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Keryx.Tests;

/// <summary>
///     The incremental generation tests (ADR 0001, Incremental generation rules) run once through the testing library with
///     <see cref="GeneratorTestContextBuilder.WithIncrementalTracking" />, edit the compilation with <see cref="GeneratorTestResults.RerunAsync" />,
///     and read the tracked steps of one generator. A rerun reports a syntax-provider input it did not touch as <c>Unchanged</c>, not
///     <c>Cached</c>, so the assertions accept both.
/// </summary>
internal static class IncrementalRuns
{
    /// <summary>The run result of <typeparamref name="TGenerator" />, with its tracked steps.</summary>
    public static GeneratorRunResult RunResult<TGenerator>(this GeneratorTestResults results) =>
        results.Results[typeof(TGenerator)].RunResult ?? throw new InvalidOperationException($"{typeof(TGenerator).Name} has no run result");

    /// <summary>The sources <typeparamref name="TGenerator" /> generated, by hint name.</summary>
    public static Dictionary<string, string> GeneratedSources<TGenerator>(this GeneratorTestResults results) =>
        results.RunResult<TGenerator>().GeneratedSources.ToDictionary(static s => s.HintName, static s => s.SourceText.ToString(), StringComparer.Ordinal);

    /// <summary>Runs the generators again with one more source file, parsed with the results' parse options.</summary>
    public static Task<GeneratorTestResults> RerunWithSourceAsync(this GeneratorTestResults results, string path, string source, CancellationToken cancellationToken) =>
        results.RerunAsync(
            compilation => compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, results.ParseOptions, path, cancellationToken: cancellationToken)),
            cancellationToken: cancellationToken
        );

    /// <summary>The reasons of every output of the steps.</summary>
    public static IncrementalStepRunReason[] Reasons(this IEnumerable<IncrementalGeneratorRunStep> steps) => [.. steps.SelectMany(static s => s.Outputs).Select(static o => o.Reason)];

    /// <summary>Whether a reason means the step did no new work.</summary>
    public static bool IsCachedOrUnchanged(this IncrementalStepRunReason reason) => reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged;

    /// <summary>The reasons, for an assertion message.</summary>
    public static string Describe(this IEnumerable<IncrementalStepRunReason> reasons) => string.Join(", ", reasons.Select(static r => r.ToString()));
}
