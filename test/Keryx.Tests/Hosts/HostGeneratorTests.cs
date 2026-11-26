using Keryx.Analyzers;
using Keryx.Analyzers.Diagnostics;
using Keryx.Analyzers.Hosts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Keryx.Tests.Hosts;

/// <summary>
///     Snapshots and diagnostics of the host generator (ADR 0001, Generated host; the Phase 3 brief): the mediator, <c>AddKeryx()</c>, the
///     host namespace, the dispatch threshold, KRX022, KRX030 (referenced requests only: the host's own are KRX001's) and KRX031.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class HostGeneratorTests : HostTest
{
    /// <summary>Every request shape Phase 3 dispatches, and every kind of injected parameter.</summary>
    internal const string Requests = """
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
        await VerifyHost(result);
    }

    /// <summary>
    ///     The handler partials and manifest a host's handler generator emits for <see cref="Requests" />, in the handler generator's own
    ///     snapshot shape (<c>Verify(result)</c>); <see cref="Should_Generate_The_Mediator_And_AddKeryx" /> snapshots the host output.
    /// </summary>
    [Test]
    public async Task Should_Generate_The_Handlers_Of_A_Host(CancellationToken cancellationToken)
    {
        var result = await Builder.WithRootNamespace(HostNamespace).WithKeryxHost().AddSources(Usings + Requests).Build().GenerateAsync(cancellationToken);

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
        await Assert.That(GenericDispatch.Of(mediator, "Send")).IsEqualTo(new GenericDispatch("__RequestIndex", 0, "__requestTypes"));
        await VerifyHost(result);
    }

    [Test]
    public async Task Should_Compare_Types_Linearly_Up_To_The_Threshold(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + ManyRequests(HostEmitter.DispatchThreshold)).Build().GenerateAsync(cancellationToken);

        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        var mediator = MediatorSource(result);
        await Assert.That(mediator).DoesNotContain("FrozenDictionary");
        await Assert.That(GenericDispatch.Of(mediator, "Send")).IsEqualTo(new GenericDispatch(null, HostEmitter.DispatchThreshold, ""));
    }

    [Test]
    public async Task Should_Generate_An_Empty_Mediator_For_A_Host_Without_Handlers(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings).Build().GenerateAsync(cancellationToken);

        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        await VerifyHost(result);
    }

    [Test]
    [Arguments(null)]
    [Arguments("false")]
    [Arguments("")]
    [Arguments("yes")]
    public async Task Should_Generate_Nothing_Outside_A_Host(string? keryxHost, CancellationToken cancellationToken)
    {
        // Unset (no Keryx build files), false (a class library, or a project that opts out) and anything that is not a boolean.
        var builder = keryxHost is null ? HostGenerators : HostGenerators.AddGlobalOption(BuildProperties.KeryxHost, keryxHost);
        var result = await builder.AddSources(Usings + Requests.Replace("internal partial class", "public partial class", StringComparison.Ordinal))
                                  .Build()
                                  .GenerateAsync(cancellationToken);

        var host = result.Results[typeof(HostGenerator)];
        await Assert.That(host.SyntaxTrees).IsEmpty();
        await Assert.That(host.Diagnostics).IsEmpty();
    }

    [Test]
    [Arguments("true")]
    [Arguments("True")]
    [Arguments(" TRUE ")]
    public async Task Should_Read_KeryxHost_As_A_Boolean(string keryxHost, CancellationToken cancellationToken)
    {
        var result = await HostGenerators.AddGlobalOption(BuildProperties.KeryxHost, keryxHost).AddSources(Usings).Build().GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(MediatorSource(result)).Contains("class KeryxMediator");
    }

    [Test]
    [Arguments("My.App", "My.App")]
    [Arguments("  Contoso.Orders.Api  ", "Contoso.Orders.Api")]
    public async Task Should_Use_The_Root_Namespace(string rootNamespace, string expected, CancellationToken cancellationToken)
    {
        var result = await Builder.WithGenerator<HostGenerator>()
                                  .WithKeryxHost()
                                  .WithRootNamespace(rootNamespace)
                                  .AddSources(Usings)
                                  .Build()
                                  .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(MediatorSource(result)).Contains($"namespace {expected}\n");
    }

    [Test]
    public async Task Should_Fall_Back_To_The_Sanitised_Assembly_Name(CancellationToken cancellationToken)
    {
        var result = await Builder.WithGenerator<HostGenerator>()
                                  .WithKeryxHost()
                                  .WithProjectName("My-App.2nd.class")
                                  .AddSources(Usings)
                                  .Build()
                                  .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(MediatorSource(result)).Contains("namespace My_App._2nd.@class\n");
    }

    [Test]
    public async Task KRX031_Is_Reported_Without_A_Location_And_Nothing_Is_Generated(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.WithTargetFramework(".NETStandard", "v2.0")
                                      .WithProjectName("Legacy.Host")
                                      .AddCompilationReferences(await OrphanLibraryAsync(cancellationToken))
                                      .AddSources(Usings + Requests)
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        // OQ-32: nothing is generated, and KRX030 is still reported next to KRX031.
        var host = result.Results[typeof(HostGenerator)];
        await Assert.That(host.SyntaxTrees).IsEmpty();
        await Assert.That(host.Diagnostics.Select(static d => d.Id)).IsEquivalentTo(["KRX030", "KRX031"]);
        var diagnostic = host.Diagnostics.Single(static d => d.Id == "KRX031");
        await Assert.That(diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
                    .IsEqualTo(
                         "'Legacy.Host' is a Keryx host (its KeryxHost MSBuild property is true), so it must target net10.0 or newer, but it targets "
                       + ".NETStandard,Version=v2.0; set <KeryxHost>false</KeryxHost> if it is not meant to be one"
                     );
        // A host is an MSBuild property, so the project-wide diagnostic has no source location (ADR 0001, Diagnostics).
        await Assert.That(diagnostic.Location).IsEqualTo(Location.None);
    }

    [Test]
    public async Task KRX031_Is_Not_Reported_For_Net10(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.WithTargetFramework(".NETCoreApp", "v10.0")
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
        // Requests is valid C# 13; the referenced orphan shows that KRX030 is not reported either.
        var source = Usings + Requests;
        var result = await HostBuilder.WithLanguageVersion(LanguageVersion.CSharp13)
                                      .AddCompilationReferences(await OrphanLibraryAsync(cancellationToken))
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
    public async Task KRX001_Not_KRX030_For_The_Hosts_Own_Request_Types_Without_Handlers(CancellationToken cancellationToken)
    {
        // KRX001 applies in a host too (decision 2026-11-26); KRX030 is the safety net for referenced requests only, so a request declared in
        // the host is reported once.
        var result = await HostBuilder.WithAnalyzer<MissingHandlerAnalyzer>()
                                      .AddSources(
                                           Usings
                                         + """
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

        await Assert.That(result.Results[typeof(HostGenerator)].Diagnostics).IsEmpty();
        var diagnostics = result.AnalyzerResults[typeof(MissingHandlerAnalyzer)].Diagnostics;
        await Assert.That(diagnostics.Select(static d => d.Id).Distinct()).IsEquivalentTo(["KRX001"]);
        await Assert.That(diagnostics.Select(static d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)))
                    .IsEquivalentTo(
                         [
                             "Request type 'Orphan' has no handler in this assembly",
                             "Request type 'OrphanStream' has no handler in this assembly",
                             "Request type 'OrphanStruct' has no handler in this assembly",
                             "Request type 'Outer.Nested' has no handler in this assembly",
                             "Request type 'Split' has no handler in this assembly",
                         ]
                     );
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
        var result = await HostBuilder.AddSources(Usings + "namespace Sample;\n" + source).Build().GenerateAsync(cancellationToken);

        await Assert.That(result.Results[typeof(HostGenerator)].Diagnostics).IsEmpty();
    }

    [Test]
    [Arguments(ReportDiagnostic.Suppress, null)]
    [Arguments(ReportDiagnostic.Warn, DiagnosticSeverity.Warning)]
    [Arguments(ReportDiagnostic.Default, DiagnosticSeverity.Error)]
    public async Task KRX030_Severity_Can_Be_Changed(ReportDiagnostic option, DiagnosticSeverity? expected, CancellationToken cancellationToken)
    {
        // What <WarningsAsErrors>, /nowarn or a severity option become: the compilation's specific diagnostic options.
        var result = await HostBuilder.WithDiagnosticOption("KRX030", option)
                                      .AddCompilationReferences(await OrphanLibraryAsync(cancellationToken))
                                      .AddSources(Usings + "namespace Sample;")
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        await Assert.That(Krx030Severity(result)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(ReportDiagnostic.Suppress, false, DiagnosticSeverity.Error)]
    [Arguments(ReportDiagnostic.Warn, false, DiagnosticSeverity.Error)]
    [Arguments(ReportDiagnostic.Suppress, true, null)]
    [Arguments(ReportDiagnostic.Warn, true, DiagnosticSeverity.Warning)]
    public async Task KRX030_Without_A_Location_Takes_Only_The_Global_Severity(
        ReportDiagnostic option,
        bool global,
        DiagnosticSeverity? expected,
        CancellationToken cancellationToken
    )
    {
        // A referenced request's KRX030 has no source location (a host is an MSBuild property, not an attribute to report on). A per-tree
        // option, what `[*.cs] dotnet_diagnostic.KRX030.severity` in an .editorconfig becomes, applies only to a diagnostic reported on the
        // tree itself (OQ-35); a global one, what a global AnalyzerConfig (.globalconfig) becomes, applies to it.
        var severity = option switch
        {
            ReportDiagnostic.Suppress => "none",
            ReportDiagnostic.Warn => "warning",
            _ => throw new ArgumentOutOfRangeException(nameof(option), option, null),
        };
        var builder = HostBuilder.AddCompilationReferences(await OrphanLibraryAsync(cancellationToken)).AddSource("Host.cs", Usings + "namespace Sample;");
        builder = global
            ? builder.AddGlobalOption("dotnet_diagnostic.KRX030.severity", severity)
            : builder.AddOption("Host.cs", "dotnet_diagnostic.KRX030.severity", severity);
        var result = await builder.Build().GenerateAsync(cancellationToken);

        // The per-file option names the host's only source file.
        await Assert.That(result.InputCompilation.SyntaxTrees.Select(static t => t.FilePath)).IsEquivalentTo(["Host.cs"]);
        await Assert.That(Krx030Severity(result)).IsEqualTo(expected);
    }

    private static DiagnosticSeverity? Krx030Severity(GeneratorTestResults result) =>
        result.Results[typeof(HostGenerator)].Diagnostics.Where(static d => d.Id == "KRX030").Select(static d => (DiagnosticSeverity?)d.Severity).SingleOrDefault();

    [Test]
    public async Task Obsolete_Requests_And_Handler_Classes_Do_Not_Warn_In_Generated_Code(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(
                                           Usings
                                         + """
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
                                      .AddSources(Usings + "namespace Sample;\npublic interface IClock { }\npublic sealed record A1 : IRequest<int>;\n" + handler)
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
                                      .AddSources(Usings + "namespace Sample;\n" + handler)
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

    /// <summary>
    ///     A referenced library with a request and no handler, for KRX030 (built without the analyzers, as if KRX001 were suppressed there).
    /// </summary>
    private Task<GeneratorTestResults> OrphanLibraryAsync(CancellationToken cancellationToken) =>
        CompileLibraryAsync("Orphans", "namespace Orphans; public sealed record Orphan : Keryx.IRequest<int>;", cancellationToken);

    internal static string ManyRequests(int count)
    {
        var builder = new System.Text.StringBuilder("namespace Sample;\n\n");
        for (var i = 0; i < count; i++)
        {
            builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"public sealed record R{i:00}(int Value) : IRequest<int>;\n");
            builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"public static partial class R{i:00}Handler {{ private static int Handle(R{i:00} request) => request.Value + {i}; }}\n");
        }

        return builder.ToString();
    }

    private static string MediatorSource(GeneratorTestResults result) => TestSources.HostSource(result, HostEmitter.MediatorHintName);
}
