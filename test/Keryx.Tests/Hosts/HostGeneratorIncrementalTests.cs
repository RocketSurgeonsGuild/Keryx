using Keryx.Analyzers;
using Keryx.Analyzers.Hosts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Keryx.Tests.Hosts;

/// <summary>
///     The incremental rules for the host pipeline (ADR 0001, Incremental generation rules): an unrelated edit leaves every named step and
///     both host sources <c>Cached</c> or <c>Unchanged</c>; adding a request handler, a global behavior or a notification type a polymorphic
///     handler receives produces <c>Modified</c> host sources. The diagnostics
///     output reads the compilation to report on syntax trees (OQ-35), so it reruns on every edit and is not asserted; the diagnostics
///     themselves are a cached step. With references, every step after the scan stays cached on an unrelated edit, and swapping in a reference
///     whose manifest changed produces <c>Modified</c> host sources.
/// </summary>
public class HostGeneratorIncrementalTests
{
    private const string Host = """
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

        public interface IDomainEvent : INotification { }
        public sealed record OrderPlaced : IDomainEvent;
        public sealed record Unheard : INotification;

        public static partial class AuditDomainEvents
        {
            private static ValueTask Handle(IDomainEvent notification) => default;
        }
        """;

    /// <summary>Steps fed directly by the compilation always run, and are not asserted (ADR 0001).</summary>
    private static readonly string[] CompilationFedSteps =
    [
        TrackingNames.HostCompilation,
        TrackingNames.HostScanInputs,
        TrackingNames.HostScanRequests,
        TrackingNames.HostCompositionAndCompilation,
        TrackingNames.HostDiagnosticsAndCompilation,
    ];

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
        TrackingNames.HostNotificationCandidates,
        TrackingNames.HostNotificationModels,
        TrackingNames.HostNotifications,
        TrackingNames.HostNotificationList,
        TrackingNames.HostScanInputs,
        TrackingNames.HostScanRequests,
        TrackingNames.HostScanRequest,
        TrackingNames.HostSettingsAndHandlers,
        TrackingNames.HostSettingsHandlersAndNotifications,
        TrackingNames.HostCompositionInputs,
        TrackingNames.HostCompositionAndCompilation,
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

    [Test]
    public async Task Adding_A_Global_Behavior_Modifies_The_Host_Sources(CancellationToken cancellationToken)
    {
        var compilation = Compile(Host);
        var driver = Run(compilation, cancellationToken);

        // A global behavior in another file changes every pipeline although no handler's syntax changed.
        driver = driver.RunGenerators(
            compilation.AddSyntaxTrees(
                CSharpSyntaxTree.ParseText(
                    """
                    namespace Sample;

                    [Keryx.RequestBehavior]
                    [Keryx.GlobalBehavior]
                    public static class Audit
                    {
                        public static System.Threading.Tasks.ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, System.Threading.CancellationToken cancellationToken)
                            where TRequest : Keryx.IRequest<TResponse>
                            where TNext : struct, Keryx.INext<TResponse> => next.InvokeAsync(cancellationToken);
                    }
                    """,
                    ParseOptions,
                    "Audit.cs",
                    cancellationToken: cancellationToken
                )
            ),
            cancellationToken
        );
        var result = driver.GetRunResult().Results.Single();

        var outputs = SourceOutputSteps(result).SelectMany(static s => s.Outputs).Select(static o => o.Reason).ToArray();
        await Assert.That(outputs).Contains(IncrementalStepRunReason.Modified);
        await Assert.That(result.GeneratedSources.Single(static s => s.HintName == HostEmitter.MediatorHintName).SourceText.ToString())
                    .Contains("global::Sample.Audit.Handle<global::Sample.CreateOrder.Command, global::System.Guid, ");
    }

    [Test]
    public async Task Adding_A_Notification_Type_A_Polymorphic_Handler_Receives_Modifies_The_Host_Sources(CancellationToken cancellationToken)
    {
        var compilation = Compile(Host);
        var driver = Run(compilation, cancellationToken);

        // No handler's syntax changes, but the polymorphic AuditDomainEvents now receives one more concrete type.
        driver = driver.RunGenerators(
            compilation.AddSyntaxTrees(
                CSharpSyntaxTree.ParseText("namespace Sample; public sealed record Shipped : IDomainEvent;", ParseOptions, "Shipped.cs", cancellationToken: cancellationToken)
            ),
            cancellationToken
        );
        var result = driver.GetRunResult().Results.Single();

        var outputs = SourceOutputSteps(result).SelectMany(static s => s.Outputs).Select(static o => o.Reason).ToArray();
        await Assert.That(outputs).Contains(IncrementalStepRunReason.Modified);
        await Assert.That(result.GeneratedSources.Single(static s => s.HintName == HostEmitter.MediatorHintName).SourceText.ToString())
                    .Contains("private __Notification1 __CreateNotification1()");
    }

    [Test]
    public async Task With_References_An_Unrelated_Edit_Leaves_Every_Step_After_The_Scan_Cached_Or_Unchanged(CancellationToken cancellationToken)
    {
        var contracts = TestLibraries.Reference("Contracts", CrossAssemblyTests.Contracts);
        var orders = TestLibraries.Reference("Orders", CrossAssemblyTests.Orders, [contracts]);
        var compilation = Compile(Host).AddReferences(contracts, orders);
        var driver = Run(compilation, cancellationToken);
        await Assert.That(driver.GetRunResult().Results.Single().GeneratedSources.Single(static s => s.HintName == HostEmitter.MediatorHintName).SourceText.ToString())
                    .Contains("Send(global::Orders.CreateOrder request");

        driver = driver.RunGenerators(
            compilation.AddSyntaxTrees(
                CSharpSyntaxTree.ParseText("namespace Sample; public sealed class Unrelated { public int Value { get; set; } }", ParseOptions, "Unrelated.cs", cancellationToken: cancellationToken)
            ),
            cancellationToken
        );
        var result = driver.GetRunResult().Results.Single();

        foreach (var name in (string[])[Specular.Scanning.ScanTrackingNames.Output, .. NamedSteps.Except(CompilationFedSteps)])
        {
            var reasons = result.TrackedSteps[name].SelectMany(static s => s.Outputs).Select(static o => o.Reason).ToArray();
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
    public async Task Swapping_In_A_Reference_With_A_Changed_Manifest_Modifies_The_Host_Sources(CancellationToken cancellationToken)
    {
        var contracts = TestLibraries.Reference("Contracts", CrossAssemblyTests.Contracts);
        var orders = TestLibraries.Reference("Orders", CrossAssemblyTests.Orders, [contracts]);
        var compilation = Compile(Host).AddReferences(contracts, orders);
        var driver = Run(compilation, cancellationToken);

        // The same library, rebuilt with one more handler: its manifest changed.
        var rebuilt = TestLibraries.Reference(
            "Orders",
            CrossAssemblyTests.Orders + "\npublic sealed record Archive : IRequest;\npublic static partial class ArchiveHandler { private static void Handle(Archive request) { } }\n",
            [contracts]
        );
        driver = driver.RunGenerators(compilation.ReplaceReference(orders, rebuilt), cancellationToken);
        var result = driver.GetRunResult().Results.Single();

        await Assert.That(result.TrackedSteps[Specular.Scanning.ScanTrackingNames.Output].SelectMany(static s => s.Outputs).Select(static o => o.Reason))
                    .Contains(IncrementalStepRunReason.Modified);
        var outputs = SourceOutputSteps(result).SelectMany(static s => s.Outputs).Select(static o => o.Reason).ToArray();
        await Assert.That(outputs).Contains(IncrementalStepRunReason.Modified);
        await Assert.That(result.GeneratedSources.Single(static s => s.HintName == HostEmitter.MediatorHintName).SourceText.ToString())
                    .Contains("Send(global::Orders.Archive request");
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
                optionsProvider: BuildProperties.Host,
                driverOptions: new(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true)
            )
           .RunGenerators(compilation, cancellationToken);
}
