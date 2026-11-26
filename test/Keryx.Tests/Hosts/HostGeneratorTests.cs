using Keryx.Analyzers;
using Keryx.Analyzers.Diagnostics;
using Keryx.Analyzers.Hosts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Keryx.Tests.Hosts;

/// <summary>
///     Snapshots and diagnostics of the host generator (ADR 0001, Generated host; the Phase 3 brief): the mediator, <c>AddKeryx()</c>, the
///     host namespace, the dispatch threshold, KRX022, KRX030 (with <c>ExternallyHandled</c>), KRX031 and KRX036 for <c>ExternallyHandled</c>.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class HostGeneratorTests : HostTest
{
    /// <summary>Every request shape Phase 3 dispatches, and every kind of injected parameter.</summary>
    internal const string Requests = """
        [assembly: KeryxHost]

        namespace Sample;

        public interface IClock { DateTimeOffset Now { get; } }
        public interface IGreeter { string Greet(string name); }
        public enum Mode { Fast = 1, Careful = 2 }

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

        public readonly record struct Tick(int Value) : IRequest<int>;

        public static partial class TickHandler
        {
            private static int Handle(Tick request) => request.Value + 1;
        }

        public sealed record Touch : IRequest;

        public static partial class TouchHandler
        {
            private static void Handle(Touch request) { }
        }

        public record Animal(string Name) : IRequest<string?>;

        internal partial class AnimalHandler
        {
            private static Task<string?> Handle(Animal request, IServiceProvider services, IMediator mediator) => Task.FromResult<string?>(request.Name);
        }

        public sealed record Lookup(string Key) : IRequest<List<string>>;

        public partial class LookupHandler
        {
            private static List<string> Handle(
                Lookup request,
                [FromKeyedServices("primary")] IGreeter greeter,
                [FromKeyedServices("backup")] IGreeter? backup = null,
                IClock? clock = null,
                int retries = 3,
                int? limit = null,
                Mode mode = Mode.Careful,
                [FromKeyedServices(1)] int keyed = 2,
                long? required = default) => [];
        }

        public sealed record Placed : INotification;

        public partial class OnPlaced
        {
            private static ValueTask Handle(Placed notification, IClock clock) => default;
        }
        """;

    [Test]
    public async Task Should_Generate_The_Mediator_And_AddKeryx(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + Requests).Build().GenerateAsync(cancellationToken);

        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        await Verify(result);
    }

    [Test]
    public async Task Should_Dispatch_Through_A_FrozenDictionary_Above_The_Threshold(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + ManyRequests(HostEmitter.DispatchThreshold + 1)).Build().GenerateAsync(cancellationToken);

        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        var mediator = MediatorSource(result);
        await Assert.That(mediator).Contains("FrozenDictionary");
        await Verify(mediator);
    }

    [Test]
    public async Task Should_Compare_Types_Linearly_Up_To_The_Threshold(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + ManyRequests(HostEmitter.DispatchThreshold)).Build().GenerateAsync(cancellationToken);

        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        var mediator = MediatorSource(result);
        await Assert.That(mediator).DoesNotContain("FrozenDictionary");
        await Assert.That(mediator.Split("if (requestType == typeof(").Length - 1).IsEqualTo(HostEmitter.DispatchThreshold);
    }

    [Test]
    public async Task Should_Generate_An_Empty_Mediator_For_A_Host_Without_Handlers(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + "[assembly: KeryxHost]").Build().GenerateAsync(cancellationToken);

        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        await Verify(result);
    }

    [Test]
    public async Task Should_Generate_Nothing_Outside_A_Host(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + Requests.Replace("[assembly: KeryxHost]", "", StringComparison.Ordinal).Replace("internal partial class", "public partial class", StringComparison.Ordinal))
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        var host = result.Results[typeof(HostGenerator)];
        await Assert.That(host.SyntaxTrees).IsEmpty();
        await Assert.That(host.Diagnostics).IsEmpty();
    }

    [Test]
    [Arguments("My.App", "My.App")]
    [Arguments("  Contoso.Orders.Api  ", "Contoso.Orders.Api")]
    public async Task Should_Use_The_Root_Namespace(string rootNamespace, string expected, CancellationToken cancellationToken)
    {
        var result = await Builder.WithGenerator<HostGenerator>()
                                  .AddGlobalOption("build_property.RootNamespace", rootNamespace)
                                  .AddSources(Usings + "[assembly: KeryxHost]")
                                  .Build()
                                  .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(MediatorSource(result)).Contains($"namespace {expected}\n");
    }

    [Test]
    public async Task Should_Fall_Back_To_The_Sanitised_Assembly_Name(CancellationToken cancellationToken)
    {
        var result = await Builder.WithGenerator<HostGenerator>()
                                  .WithProjectName("My-App.2nd.class")
                                  .AddSources(Usings + "[assembly: KeryxHost]")
                                  .Build()
                                  .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(MediatorSource(result)).Contains("namespace My_App._2nd.@class\n");
    }

    [Test]
    public async Task KRX031_Is_Reported_On_The_Host_Attribute_And_Nothing_Is_Generated(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddGlobalOption("build_property.TargetFrameworkIdentifier", ".NETStandard")
                                      .AddGlobalOption("build_property.TargetFrameworkVersion", "v2.0")
                                      .WithProjectName("Legacy.Host")
                                      .AddSources(Usings + Requests + Orphan)
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        // OQ-32: nothing is generated, and KRX030 is still reported next to KRX031.
        var host = result.Results[typeof(HostGenerator)];
        await Assert.That(host.SyntaxTrees).IsEmpty();
        await Assert.That(host.Diagnostics.Select(static d => d.Id)).IsEquivalentTo(["KRX030", "KRX031"]);
        var diagnostic = host.Diagnostics.Single(static d => d.Id == "KRX031");
        await Assert.That(diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
                    .IsEqualTo("'Legacy.Host' is a Keryx host ([assembly: KeryxHost]), so it must target net10.0 or newer, but it targets .NETStandard,Version=v2.0");
        // Host diagnostics carry an equatable LocationInfo, turned back into a file location when reported (ADR 0001, Diagnostics).
        var input = result.InputSyntaxTrees.Single(t => t.FilePath == diagnostic.Location.GetLineSpan().Path);
        await Assert.That(input.GetText(cancellationToken).ToString(diagnostic.Location.SourceSpan)).IsEqualTo("KeryxHost");
    }

    [Test]
    public async Task KRX031_Is_Not_Reported_For_Net10(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddGlobalOption("build_property.TargetFrameworkIdentifier", ".NETCoreApp")
                                      .AddGlobalOption("build_property.TargetFrameworkVersion", "v10.0")
                                      .AddSources(Usings + Requests)
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        var host = result.Results[typeof(HostGenerator)];
        await Assert.That(host.Diagnostics).IsEmpty();
        await Assert.That(host.SyntaxTrees.Select(static t => Path.GetFileName(t.FilePath)))
                    .IsEquivalentTo([HostEmitter.MediatorHintName, HostEmitter.ServiceCollectionExtensionsHintName]);
    }

    [Test]
    public async Task KRX022_A_Host_Below_CSharp_14_Gets_No_Host_Sources(CancellationToken cancellationToken)
    {
        // Requests is valid C# 13; the orphan shows that KRX030 is not reported either.
        var source = Usings + Requests + Orphan;
        var result = await HostBuilder.WithLanguageVersion(LanguageVersion.CSharp13)
                                      .WithAnalyzer<LanguageVersionAnalyzer>()
                                      .AddSources(source)
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        var host = result.Results[typeof(HostGenerator)];
        await Assert.That(host.SyntaxTrees).IsEmpty();
        await Assert.That(host.Diagnostics).IsEmpty();
        await Assert.That(result.Results[typeof(HandlerGenerator)].SyntaxTrees).IsEmpty();
        await Assert.That(result.AnalyzerResults[typeof(LanguageVersionAnalyzer)].Diagnostics.Select(static d => d.Id)).IsEquivalentTo(["KRX022"]);

        // KRX022 is the only error in the whole compilation.
        var errors = result.FinalDiagnostics.Concat(result.AnalyzerResults.Values.SelectMany(static r => r.Diagnostics))
                           .Where(static d => d.Severity == DiagnosticSeverity.Error)
                           .Select(static d => d.Id)
                           .Distinct();
        await Assert.That(errors).IsEquivalentTo(["KRX022"]);
    }

    [Test]
    public async Task KRX030_Request_Types_Without_Handlers_In_The_Host(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(
                                           Usings
                                         + """
                                           [assembly: KeryxHost]

                                           namespace Sample;

                                           public sealed record Orphan : IRequest<int>;
                                           public readonly record struct OrphanStruct : IRequest;
                                           public sealed record OrphanStream : IStreamRequest<int>;
                                           public static class Outer { public sealed record Nested : IRequest<string>; }
                                           public partial record Split : IRequest<int>;
                                           public partial record Split { public int Value { get; init; } }
                                           """
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        var diagnostics = result.Results[typeof(HostGenerator)].Diagnostics;
        await Assert.That(diagnostics.Select(static d => d.Id).Distinct()).IsEquivalentTo(["KRX030"]);
        await Assert.That(diagnostics.Select(static d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)))
                    .IsEquivalentTo(
                         [
                             "Request type 'Orphan' has no handler in this host",
                             "Request type 'OrphanStream' has no handler in this host",
                             "Request type 'OrphanStruct' has no handler in this host",
                             "Request type 'Outer.Nested' has no handler in this host",
                             "Request type 'Split' has no handler in this host",
                         ]
                     );
        await Assert.That(diagnostics.All(static d => d.Severity == DiagnosticSeverity.Error && d.Location.GetLineSpan().Path.EndsWith(".cs", StringComparison.Ordinal))).IsTrue();
        await Assert.That(diagnostics[0].Descriptor.HelpLinkUri).IsEqualTo(Descriptors.HelpLinkBase + "krx030");
    }

    [Test]
    [Arguments("public sealed record Handled : IRequest<int>; public static partial class H { private static int Handle(Handled request) => 1; }")]
    [Arguments("public sealed record Streamed : IStreamRequest<int>; public static partial class H { private static async IAsyncEnumerable<int> Handle(Streamed request) { await Task.Yield(); yield return 1; } }")]
    [Arguments("public sealed record Malformed : IRequest<int>; public static partial class H { public static int Handle(Malformed request) => 1; }")]
    [Arguments("public abstract record Base : IRequest<int>; public interface IQuery : IRequest<int> { }")]
    [Arguments("public sealed record Generic<T> : IRequest<T>;")]
    [Arguments("public sealed record Happened : INotification;")]
    public async Task KRX030_Handled_Or_Not_A_Request(string source, CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;\n" + source).Build().GenerateAsync(cancellationToken);

        await Assert.That(result.Results[typeof(HostGenerator)].Diagnostics).IsEmpty();
    }

    [Test]
    public async Task ExternallyHandled_Silences_KRX030_For_The_Named_Type_Only(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(
                                           Usings
                                         + """
                                           [assembly: KeryxHost]
                                           [assembly: ExternallyHandled(typeof(Sample.Orphan))]
                                           [assembly: ExternallyHandled(typeof(Sample.OrphanStream))]

                                           namespace Sample;

                                           public sealed record Orphan : IRequest<int>;
                                           public sealed record OrphanStream : IStreamRequest<int>;
                                           public sealed record Other : IRequest<int>;
                                           """
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        await Assert.That(result.Results[typeof(HostGenerator)].Diagnostics.Select(static d => $"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}"))
                    .IsEquivalentTo(["KRX030: Request type 'Other' has no handler in this host"]);
    }

    [Test]
    public async Task KRX036_For_ExternallyHandled_On_A_Handled_Request_Or_A_Type_That_Is_Not_A_Request(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(
                                           Usings
                                         + """
                                           [assembly: KeryxHost]
                                           [assembly: ExternallyHandled(typeof(Sample.Handled))]
                                           [assembly: ExternallyHandled(typeof(Sample.Happened))]
                                           [assembly: ExternallyHandled(typeof(string))]
                                           [assembly: ExternallyHandled(typeof(IRequest<int>))]
                                           [assembly: ExternallyHandled(typeof(Sample.Base))]
                                           [assembly: ExternallyHandled(typeof(Sample.Generic<>))]

                                           namespace Sample;

                                           public sealed record Handled : IRequest<int>;
                                           public static partial class H { private static int Handle(Handled request) => 1; }
                                           public sealed record Happened : INotification;
                                           public abstract record Base : IRequest<int>;
                                           public sealed record Generic<T> : IRequest<int>;
                                           """
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        var diagnostics = result.Results[typeof(HostGenerator)].Diagnostics;
        await Assert.That(diagnostics.Select(static d => $"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}"))
                    .IsEquivalentTo(
                         [
                             "KRX036: [assembly: ExternallyHandled(typeof(Handled))] has no effect, because 'Handled' has a handler in this host",
                             "KRX036: [assembly: ExternallyHandled(typeof(Happened))] has no effect, because 'Happened' is not a request or stream request",
                             "KRX036: [assembly: ExternallyHandled(typeof(string))] has no effect, because 'string' is not a request or stream request",
                             "KRX036: [assembly: ExternallyHandled(typeof(IRequest<int>))] has no effect, because 'IRequest<int>' is not a request or stream request",
                             "KRX036: [assembly: ExternallyHandled(typeof(Base))] has no effect, because 'Base' is not a request or stream request",
                             "KRX036: [assembly: ExternallyHandled(typeof(Generic<>))] has no effect, because 'Generic<>' is not a request or stream request",
                         ]
                     );
        await Assert.That(diagnostics.All(static d => d.Severity == DiagnosticSeverity.Warning && d.Location.IsInSource)).IsTrue();
        foreach (var diagnostic in diagnostics)
        {
            await Assert.That(diagnostic.Location.SourceTree!.GetText(cancellationToken).ToString(diagnostic.Location.SourceSpan)).StartsWith("ExternallyHandled");
        }
    }

    [Test]
    [Arguments(ReportDiagnostic.Suppress, null)]
    [Arguments(ReportDiagnostic.Warn, DiagnosticSeverity.Warning)]
    [Arguments(ReportDiagnostic.Default, DiagnosticSeverity.Error)]
    public async Task KRX030_Severity_Can_Be_Changed(ReportDiagnostic option, DiagnosticSeverity? expected, CancellationToken cancellationToken)
    {
        // What dotnet_diagnostic.KRX030.severity in .editorconfig becomes; the testing helpers do not pass severity options on.
        var compilation = DirectCompilation.Create(Usings + "[assembly: KeryxHost]\nnamespace Sample;\npublic sealed record Orphan : IRequest<int>;");
        compilation = compilation.WithOptions(compilation.Options.WithSpecificDiagnosticOptions([new("KRX030", option)]));

        _ = CSharpGeneratorDriver.Create([new HostGenerator().AsSourceGenerator()], parseOptions: DirectCompilation.ParseOptions)
                             .RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics, cancellationToken);

        await Assert.That(diagnostics.Where(static d => d.Id == "KRX030").Select(static d => (DiagnosticSeverity?)d.Severity).SingleOrDefault()).IsEqualTo(expected);
    }

    [Test]
    [Arguments(ReportDiagnostic.Suppress, null)]
    [Arguments(ReportDiagnostic.Warn, DiagnosticSeverity.Warning)]
    public async Task KRX030_Severity_Can_Be_Changed_Per_File(ReportDiagnostic option, DiagnosticSeverity? expected, CancellationToken cancellationToken)
    {
        // What `[*.cs] dotnet_diagnostic.KRX030.severity` in an .editorconfig becomes: a per-tree option, which applies only to a diagnostic
        // reported on the tree itself (OQ-35), not on a file location without one.
        var compilation = DirectCompilation.Create(Usings + "[assembly: KeryxHost]\nnamespace Sample;\npublic sealed record Orphan : IRequest<int>;");
        compilation = compilation.WithOptions(compilation.Options.WithSyntaxTreeOptionsProvider(new TreeOptions(compilation.SyntaxTrees.Single(), "KRX030", option)));

        _ = CSharpGeneratorDriver.Create([new HostGenerator().AsSourceGenerator()], parseOptions: DirectCompilation.ParseOptions)
                             .RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics, cancellationToken);

        await Assert.That(diagnostics.Where(static d => d.Id == "KRX030").Select(static d => (DiagnosticSeverity?)d.Severity).SingleOrDefault()).IsEqualTo(expected);
    }

    [Test]
    public async Task Obsolete_Requests_And_Handler_Classes_Do_Not_Warn_In_Generated_Code(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(
                                           Usings
                                         + """
                                           [assembly: KeryxHost]
                                           namespace Sample;
                                           public interface IClock { }
                                           public sealed record A1 : IRequest<int>;
                                           [Obsolete("old")] public sealed record A2 : IRequest<int>;
                                           [Obsolete("old")] public sealed record A3 : IRequest<int>;
                                           [Obsolete("old")] public sealed class OldService { }
                                           [Obsolete("old")] public partial class ObsoleteHandler { private static int Handle(A1 r, IClock clock) => 1; }
                                           #pragma warning disable CS0612, CS0618 // the user's own references, which they can suppress
                                           public static partial class H2 { private static int Handle(A2 r) => 2; }
                                           public partial class H3 { private static int Handle(A3 r, OldService service) => 3; }
                                           #pragma warning restore CS0612, CS0618
                                           """
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Id is "CS0612" or "CS0618")).IsEmpty();
        await Assert.That(MediatorSource(result)).Contains("Send(global::Sample.A2 request");
    }

    [Test]
    [Arguments("public abstract partial class Constructed { private static int Handle(A1 r, IClock clock) => 1; }")]
    [Arguments("public partial class Constructed { public required string Name { get; init; } private static int Handle(A1 r, IClock clock) => 1; }")]
    [Arguments("public abstract class WithRequired { public required string Name { get; init; } } public partial class Constructed : WithRequired { private static int Handle(A1 r, IClock clock) => 1; }")]
    public async Task A_Handler_Class_The_Host_Cannot_Construct_Is_KRX007_And_Not_Generated(string handler, CancellationToken cancellationToken)
    {
        // OQ-34: without KRX007, `new Constructed(...)` in AddKeryx would fail with CS0144 or CS9035 in generated code.
        var result = await HostBuilder.WithAnalyzer<HandlerAnalyzer>()
                                      .AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;\npublic interface IClock { }\npublic sealed record A1 : IRequest<int>;\n" + handler)
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        // KRX007 is the only error: no CS0144/CS9035 in generated code.
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.Id)).IsEquivalentTo(["KRX007"]);
        await Assert.That(result.AnalyzerResults[typeof(HandlerAnalyzer)].Diagnostics.Select(static d => d.Id)).IsEquivalentTo(["KRX007"]);
        // The request counts as handled by a malformed handler: no KRX030, and nothing dispatched or registered for it.
        await Assert.That(result.Results[typeof(HostGenerator)].Diagnostics).IsEmpty();
        await Assert.That(MediatorSource(result)).DoesNotContain("A1");
        await Assert.That(result.Results[typeof(HandlerGenerator)].SyntaxTrees.Where(static t => t.FilePath.Contains("Constructed", StringComparison.Ordinal))).IsEmpty();
    }

    [Test]
    [Arguments("internal static partial class Hidden { private sealed record Secret : IRequest<int>; private static int Handle(Secret s) => 1; }")]
    [Arguments("public sealed record Visible : IRequest<int>; internal partial class Hidden { private interface IHidden { } private static int Handle(Visible v, IHidden hidden) => 1; }")]
    public async Task A_Handler_Whose_Signature_Is_Narrower_Than_Internal_Is_KRX016_In_A_Host(string handler, CancellationToken cancellationToken)
    {
        var result = await HostBuilder.WithAnalyzer<HandlerAnalyzer>()
                                      .AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;\n" + handler)
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        // OQ-24: the host skips the handler, and the analyzer says why; its request counts as handled, so no KRX030 joins it.
        await Assert.That(result.AnalyzerResults[typeof(HandlerAnalyzer)].Diagnostics.Select(static d => d.Id)).IsEquivalentTo(["KRX016"]);
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.Id)).IsEquivalentTo(["KRX016"]);
        await Assert.That(result.Results[typeof(HostGenerator)].Diagnostics).IsEmpty();
        var mediator = MediatorSource(result);
        await Assert.That(mediator).DoesNotContain("Secret");
        await Assert.That(mediator).DoesNotContain("Visible");
    }

    [Test]
    public async Task A_Request_With_Two_Handlers_Is_Not_Dispatched(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(
                                           Usings
                                         + """
                                           [assembly: KeryxHost]
                                           namespace Sample;
                                           public sealed record Ping : IRequest<int>;
                                           public static partial class First { private static int Handle(Ping p) => 1; }
                                           public static partial class Second { private static int Handle(Ping p) => 2; }
                                           """
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        // KRX005 comes from the analyzer; the host adds no duplicate overloads and no KRX030.
        result.AssertCompilationWasSuccessful();
        await Assert.That(result.Results[typeof(HostGenerator)].Diagnostics).IsEmpty();
        await Assert.That(MediatorSource(result)).DoesNotContain("Send(global::Sample.Ping request");
    }

    /// <summary>A request without a handler, for KRX030.</summary>
    private const string Orphan = "\npublic sealed record Orphan : IRequest<int>;\n";

    internal static string ManyRequests(int count)
    {
        var builder = new System.Text.StringBuilder("[assembly: KeryxHost]\n\nnamespace Sample;\n\n");
        for (var i = 0; i < count; i++)
        {
            builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"public sealed record R{i:00}(int Value) : IRequest<int>;\n");
            builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"public static partial class R{i:00}Handler {{ private static int Handle(R{i:00} request) => request.Value + {i}; }}\n");
        }

        return builder.ToString();
    }

    private static string MediatorSource(Rocket.Surgery.Extensions.Testing.SourceGenerators.GeneratorTestResults result) =>
        result.Results[typeof(HostGenerator)].SyntaxTrees.Single(static t => t.FilePath.EndsWith(HostEmitter.MediatorHintName, StringComparison.Ordinal)).ToString();

    /// <summary>Per-tree diagnostic options, as the compiler builds them from an <c>.editorconfig</c> section.</summary>
    private sealed class TreeOptions(SyntaxTree optionsTree, string optionsDiagnosticId, ReportDiagnostic optionsValue) : SyntaxTreeOptionsProvider
    {
        public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken) => GeneratedKind.Unknown;

        public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            severity = optionsValue;
            return tree == optionsTree && diagnosticId == optionsDiagnosticId;
        }

        public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            severity = default;
            return false;
        }
    }
}
