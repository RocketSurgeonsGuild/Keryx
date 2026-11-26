using Keryx.Analyzers;
using Keryx.Analyzers.Hosts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Keryx.Tests.Hosts;

/// <summary>
///     The incremental rules for the host pipeline (ADR 0001, Incremental generation rules): an unrelated edit leaves every named step and
///     both host sources <c>Cached</c> or <c>Unchanged</c>; adding a request handler produces <c>Modified</c> host sources. The diagnostics
///     output reads the compilation to report on syntax trees (OQ-35), so it reruns on every edit and is not asserted; the diagnostics
///     themselves are a cached step. (The scan and manifest parts of the test come with cross-assembly discovery.)
/// </summary>
public class HostGeneratorIncrementalTests
{
    private const string Host = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Keryx;

        [assembly: KeryxHost]

        namespace Sample;

        public interface IClock { }

        public partial class CreateOrder
        {
            public sealed record Command(string Sku) : IRequest<Guid>;

            private static ValueTask<Guid> Handle(Command request, IClock clock, CancellationToken ct) => new(Guid.Empty);
        }

        public static partial class Ping
        {
            public sealed record Query : IRequest<string>;

            private static string Handle(Query request) => "pong";
        }
        """;

    /// <summary>Steps fed directly by the compilation always run, and are not asserted (ADR 0001).</summary>
    private static readonly string[] CompilationFedSteps = [TrackingNames.HostCompilation, TrackingNames.HostDiagnosticsAndCompilation];

    private static readonly string[] NamedSteps =
    [
        TrackingNames.HostLanguageVersion,
        TrackingNames.HostCompilation,
        TrackingNames.HostBuildProperties,
        TrackingNames.HostSettingsInputs,
        TrackingNames.HostSettings,
        TrackingNames.HostHandlerCandidates,
        TrackingNames.HostHandlerEntries,
        TrackingNames.HostHandlers,
        TrackingNames.HostHandlerList,
        TrackingNames.HostRequestCandidates,
        TrackingNames.HostRequestModels,
        TrackingNames.HostRequests,
        TrackingNames.HostRequestList,
        TrackingNames.HostSettingsAndHandlers,
        TrackingNames.HostSources,
        TrackingNames.HostDiagnostics,
        TrackingNames.HostDiagnosticsAndCompilation,
    ];

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    [Test]
    public async Task Every_Named_Step_Runs(CancellationToken cancellationToken)
    {
        var result = Run(Compile(Host), cancellationToken).GetRunResult().Results.Single();

        await Assert.That(result.Exception).IsNull();
        await Assert.That(result.TrackedSteps.Keys.Where(static k => k.StartsWith("Keryx.", StringComparison.Ordinal))).IsEquivalentTo(NamedSteps);
        await Assert.That(result.GeneratedSources.Select(static s => s.HintName))
                    .IsEquivalentTo([HostEmitter.MediatorHintName, HostEmitter.ServiceCollectionExtensionsHintName]);
    }

    [Test]
    public async Task An_Unrelated_Edit_Leaves_Every_Step_And_Output_Cached_Or_Unchanged(CancellationToken cancellationToken)
    {
        var compilation = Compile(Host);
        var driver = Run(compilation, cancellationToken);

        driver = driver.RunGenerators(
            compilation.AddSyntaxTrees(
                CSharpSyntaxTree.ParseText("namespace Sample; public sealed class Unrelated { public int Value { get; set; } }", ParseOptions, "Unrelated.cs", cancellationToken: cancellationToken)
            ),
            cancellationToken
        );
        var result = driver.GetRunResult().Results.Single();

        foreach (var (name, steps) in result.TrackedSteps)
        {
            if (!NamedSteps.Contains(name) || CompilationFedSteps.Contains(name))
            {
                continue;
            }

            var reasons = steps.SelectMany(static s => s.Outputs).Select(static o => o.Reason).ToArray();
            await Assert.That(reasons).IsNotEmpty().Because(name);
            await Assert.That(reasons.All(static r => r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged))
                        .IsTrue()
                        .Because($"{name}: {string.Join(", ", reasons.Select(static r => r.ToString()))}");
        }

        var outputs = SourceOutputSteps(result).SelectMany(static s => s.Outputs).Select(static o => o.Reason).ToArray();
        await Assert.That(outputs).IsNotEmpty();
        await Assert.That(outputs.All(static r => r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged))
                    .IsTrue()
                    .Because(string.Join(", ", outputs.Select(static r => r.ToString())));
    }

    [Test]
    public async Task Adding_A_Handler_Modifies_The_Host_Sources(CancellationToken cancellationToken)
    {
        var compilation = Compile(Host);
        var driver = Run(compilation, cancellationToken);

        driver = driver.RunGenerators(
            compilation.AddSyntaxTrees(
                CSharpSyntaxTree.ParseText(
                    "namespace Sample; public sealed record Pong : Keryx.IRequest<int>; public static partial class PongHandler { private static int Handle(Pong request) => 1; }",
                    ParseOptions,
                    "Pong.cs",
                    cancellationToken: cancellationToken
                )
            ),
            cancellationToken
        );
        var result = driver.GetRunResult().Results.Single();

        var outputs = SourceOutputSteps(result).SelectMany(static s => s.Outputs).Select(static o => o.Reason).ToArray();
        await Assert.That(outputs).Contains(IncrementalStepRunReason.Modified);
        await Assert.That(result.GeneratedSources.Single(static s => s.HintName == HostEmitter.MediatorHintName).SourceText.ToString())
                    .Contains("Send(global::Sample.Pong request");
    }

    /// <summary>The runs of the output that adds the host sources (fed by <see cref="TrackingNames.HostSources" />).</summary>
    private static IEnumerable<IncrementalGeneratorRunStep> SourceOutputSteps(GeneratorRunResult result) =>
        result.TrackedOutputSteps.SelectMany(static s => s.Value).Where(static s => s.Inputs.Any(static i => i.Source.Name == TrackingNames.HostSources));

    private static CSharpCompilation Compile(string source) =>
        CSharpCompilation.Create(
            "Incremental",
            [CSharpSyntaxTree.ParseText(source, ParseOptions, "Host.cs")],
            [
                .. Abstractions.ManifestContractTests.FrameworkReferences,
                MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.ComponentModel.dll")),
                MetadataReference.CreateFromFile(typeof(Unit).Assembly.Location),
            ],
            new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
        );

    private static GeneratorDriver Run(Compilation compilation, CancellationToken cancellationToken) =>
        CSharpGeneratorDriver
           .Create(
                [new HostGenerator().AsSourceGenerator()],
                parseOptions: ParseOptions,
                driverOptions: new(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true)
            )
           .RunGenerators(compilation, cancellationToken);
}
