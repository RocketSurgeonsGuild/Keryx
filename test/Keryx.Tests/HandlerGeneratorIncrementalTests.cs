using Keryx.Analyzers;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Keryx.Tests;

/// <summary>
///     The incremental test for the handler pipeline (ADR 0001, Incremental generation rules): an unrelated edit leaves every named step and
///     every source output <c>Cached</c> or <c>Unchanged</c>; an edit to a handler produces <c>Modified</c> output.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class HandlerGeneratorIncrementalTests : GeneratorTest
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
        var result = ( await GenerateAsync(cancellationToken) ).RunResult<HandlerGenerator>();

        await Assert.That(result.Exception).IsNull();
        await Assert.That(result.TrackedSteps.Keys.Where(static k => k.StartsWith("Keryx.", StringComparison.Ordinal))).IsEquivalentTo(NamedSteps);
        await Assert.That(result.GeneratedSources.Select(static s => s.HintName))
                    .IsEquivalentTo(["KeryxManifest.g.cs", CreateOrderHintName, CSharpText.HintName("Sample.OnPlaced"), CSharpText.HintName("Sample.Ping")]);
    }

    [Test]
    public async Task An_Unrelated_Edit_Leaves_Every_Step_And_Output_Cached_Or_Unchanged(CancellationToken cancellationToken)
    {
        var first = await GenerateAsync(cancellationToken);
        var result = ( await first.RerunWithSourceAsync("Unrelated.cs", "namespace Sample; public sealed class Unrelated { public int Value { get; set; } }", cancellationToken) )
           .RunResult<HandlerGenerator>();

        foreach (var (name, steps) in result.TrackedSteps)
        {
            // Roslyn tracks its own input steps as well; the rule covers the generator's named steps.
            if (!NamedSteps.Contains(name) || CompilationFedSteps.Contains(name))
            {
                continue;
            }

            var reasons = steps.Reasons();
            await Assert.That(reasons).IsNotEmpty().Because(name);
            await Assert.That(reasons.All(static r => r.IsCachedOrUnchanged())).IsTrue().Because($"{name}: {reasons.Describe()}");
        }

        var outputs = result.TrackedOutputSteps.SelectMany(static s => s.Value).Reasons();
        await Assert.That(outputs).IsNotEmpty();
        await Assert.That(outputs.All(static r => r.IsCachedOrUnchanged())).IsTrue().Because(outputs.Describe());
    }

    [Test]
    public async Task Editing_A_Handler_Modifies_Its_Output_Only(CancellationToken cancellationToken)
    {
        var first = await GenerateAsync(cancellationToken);
        var before = first.GeneratedSources<HandlerGenerator>();

        var second = await first.RerunAsync(
            static compilation =>
            {
                var oldTree = compilation.SyntaxTrees.Single();
                return compilation.ReplaceSyntaxTree(
                    oldTree,
                    oldTree.WithChangedText(SourceText.From(Handlers.Replace("IClock clock, CancellationToken ct", "IClock clock, IServiceProvider services, CancellationToken ct", StringComparison.Ordinal)))
                );
            },
            cancellationToken: cancellationToken
        );
        var result = second.RunResult<HandlerGenerator>();
        var after = second.GeneratedSources<HandlerGenerator>();

        var handlerReasons = result.TrackedSteps[TrackingNames.Handlers].Reasons();
        await Assert.That(handlerReasons.Count(static r => r == IncrementalStepRunReason.Modified)).IsEqualTo(1);
        await Assert.That(handlerReasons.Count(static r => r.IsCachedOrUnchanged())).IsEqualTo(2);

        // Every source output: exactly one Modified (CreateOrder), the other handlers and the manifest Cached or Unchanged.
        var outputs = result.TrackedOutputSteps.SelectMany(static s => s.Value).Reasons();
        await Assert.That(outputs.Count(static r => r == IncrementalStepRunReason.Modified)).IsEqualTo(1).Because(outputs.Describe());
        await Assert.That(outputs.Count(static r => r.IsCachedOrUnchanged())).IsEqualTo(outputs.Length - 1).Because(outputs.Describe());

        await Assert.That(after.Keys).IsEquivalentTo(before.Keys);
        await Assert.That(before.Keys.Where(name => before[name] != after[name])).IsEquivalentTo([CreateOrderHintName]);
        await Assert.That(after[CreateOrderHintName]).Contains("global::System.IServiceProvider services");
    }

    private Task<GeneratorTestResults> GenerateAsync(CancellationToken cancellationToken) =>
        Builder.WithNullableContext(NullableContextOptions.Enable).WithIncrementalTracking().AddSource("Handlers.cs", Handlers).Build().GenerateAsync(cancellationToken);
}
