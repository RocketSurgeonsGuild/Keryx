using Keryx.Analyzers;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Keryx.Tests;

/// <summary>
///     The incremental test for the handler pipeline (ADR 0001, Incremental generation rules): an unrelated edit leaves every named step and
///     every source output <c>Cached</c> or <c>Unchanged</c>; an edit to a handler produces <c>Modified</c> output.
/// </summary>
public class HandlerGeneratorIncrementalTests
{
    private const string Handlers = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Keryx;

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

        public sealed record Placed : INotification;

        public static partial class OnPlaced
        {
            private static ValueTask Handle(Placed notification) => default;
        }
        """;

    /// <summary>Steps fed directly by the compilation always run, and are not asserted (ADR 0001).</summary>
    private static readonly string[] CompilationFedSteps = [TrackingNames.CompilationSettings];

    private static readonly string[] NamedSteps =
    [
        TrackingNames.LanguageVersion,
        TrackingNames.CompilationSettings,
        TrackingNames.HostProperty,
        TrackingNames.HandlerCompilationAndHost,
        TrackingNames.HandlerSettingsInputs,
        TrackingNames.HandlerSettings,
        TrackingNames.HandlerCandidates,
        TrackingNames.HandlerModels,
        TrackingNames.Handlers,
        TrackingNames.HandlerSources,
    ];

    private static readonly string CreateOrderHintName = CSharpText.HintName("Sample.CreateOrder");

    [Test]
    public async Task Every_Named_Step_Runs(CancellationToken cancellationToken)
    {
        var result = Run(Compile(Handlers), cancellationToken).GetRunResult().Results.Single();

        await Assert.That(result.Exception).IsNull();
        await Assert.That(result.TrackedSteps.Keys.Where(static k => k.StartsWith("Keryx.", StringComparison.Ordinal))).IsEquivalentTo(NamedSteps);
        await Assert.That(result.GeneratedSources.Select(static s => s.HintName))
                    .IsEquivalentTo(["KeryxManifest.g.cs", CreateOrderHintName, CSharpText.HintName("Sample.OnPlaced"), CSharpText.HintName("Sample.Ping")]);
    }

    [Test]
    public async Task An_Unrelated_Edit_Leaves_Every_Step_And_Output_Cached_Or_Unchanged(CancellationToken cancellationToken)
    {
        var compilation = Compile(Handlers);
        var driver = Run(compilation, cancellationToken);

        driver = driver.RunGenerators(
            compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace Sample; public sealed class Unrelated { public int Value { get; set; } }", ParseOptions, "Unrelated.cs", cancellationToken: cancellationToken)),
            cancellationToken
        );
        var result = driver.GetRunResult().Results.Single();

        foreach (var (name, steps) in result.TrackedSteps)
        {
            // Roslyn tracks its own input steps as well; the rule covers the generator's named steps.
            if (!NamedSteps.Contains(name) || CompilationFedSteps.Contains(name))
            {
                continue;
            }

            var reasons = steps.SelectMany(static s => s.Outputs).Select(static o => o.Reason).ToArray();
            await Assert.That(reasons).IsNotEmpty().Because(name);
            await Assert.That(reasons.All(static r => r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged)).IsTrue().Because($"{name}: {string.Join(", ", reasons.Select(static r => r.ToString()))}");
        }

        var outputs = result.TrackedOutputSteps.SelectMany(static s => s.Value).SelectMany(static s => s.Outputs).Select(static o => o.Reason).ToArray();
        await Assert.That(outputs).IsNotEmpty();
        await Assert.That(outputs.All(static r => r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged)).IsTrue().Because(string.Join(", ", outputs.Select(static r => r.ToString())));
    }

    [Test]
    public async Task Editing_A_Handler_Modifies_Its_Output_Only(CancellationToken cancellationToken)
    {
        var compilation = Compile(Handlers);
        var driver = Run(compilation, cancellationToken);
        var before = Sources(driver);

        var oldTree = compilation.SyntaxTrees.Single();
        var newTree = oldTree.WithChangedText(
            Microsoft.CodeAnalysis.Text.SourceText.From(Handlers.Replace("IClock clock, CancellationToken ct", "IClock clock, IServiceProvider services, CancellationToken ct", StringComparison.Ordinal))
        );
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(oldTree, newTree), cancellationToken);
        var result = driver.GetRunResult().Results.Single();
        var after = Sources(driver);

        var handlerReasons = result.TrackedSteps[TrackingNames.Handlers].SelectMany(static s => s.Outputs).Select(static o => o.Reason).ToArray();
        await Assert.That(handlerReasons.Count(static r => r == IncrementalStepRunReason.Modified)).IsEqualTo(1);
        await Assert.That(handlerReasons.Count(static r => r is IncrementalStepRunReason.Unchanged or IncrementalStepRunReason.Cached)).IsEqualTo(2);

        // Every source output: exactly one Modified (CreateOrder), the other handlers and the manifest Cached or Unchanged.
        var outputs = result.TrackedOutputSteps.SelectMany(static s => s.Value).SelectMany(static s => s.Outputs).Select(static o => o.Reason).ToArray();
        await Assert.That(outputs.Count(static r => r == IncrementalStepRunReason.Modified)).IsEqualTo(1).Because(string.Join(", ", outputs.Select(static r => r.ToString())));
        await Assert.That(outputs.Count(static r => r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged))
                    .IsEqualTo(outputs.Length - 1)
                    .Because(string.Join(", ", outputs.Select(static r => r.ToString())));

        await Assert.That(after.Keys).IsEquivalentTo(before.Keys);
        await Assert.That(before.Keys.Where(name => before[name] != after[name])).IsEquivalentTo([CreateOrderHintName]);
        await Assert.That(after[CreateOrderHintName]).Contains("global::System.IServiceProvider services");
    }

    private static Dictionary<string, string> Sources(GeneratorDriver driver) =>
        driver.GetRunResult().Results.Single().GeneratedSources.ToDictionary(static s => s.HintName, static s => s.SourceText.ToString(), StringComparer.Ordinal);

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static CSharpCompilation Compile(string source) =>
        CSharpCompilation.Create(
            "Incremental",
            [CSharpSyntaxTree.ParseText(source, ParseOptions, "Handlers.cs")],
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
                [new HandlerGenerator().AsSourceGenerator()],
                parseOptions: ParseOptions,
                driverOptions: new(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true)
            )
           .RunGenerators(compilation, cancellationToken);
}
