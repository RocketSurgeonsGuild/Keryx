using Keryx.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Keryx.Tests.Diagnostics;

/// <summary>
///     The compilation-wide rules: KRX001, KRX005 (same assembly), KRX021, KRX022 and KRX043, each with a positive and a negative case.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class CompilationRuleTests : AnalyzerTest
{
    [Test]
    [Arguments("public sealed record Ping : IRequest<int>;")]
    [Arguments("public sealed record Ping : IStreamRequest<int>;")]
    [Arguments("public static partial class Outer { public sealed record Ping : IRequest; }")]
    public async Task KRX001_Request_Without_Handler(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<MissingHandlerAnalyzer>(source, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX001"]);
        await Assert.That(diagnostics[0].Descriptor.CustomTags).Contains("CompilationEnd");
    }

    [Test]
    [Arguments("public sealed record Ping : IRequest<int>; public static partial class H { private static int Handle(Ping p) => 1; }")]
    [Arguments("public sealed record Ping : IRequest<int>; public class Broken { public string Handle(Ping p) => \"\"; }")]
    [Arguments("[assembly: KeryxContracts] public sealed record Ping : IRequest<int>;")]
    [Arguments("[assembly: KeryxHost] public sealed record Ping : IRequest<int>;")]
    [Arguments("public abstract record Base : IRequest<int>; public interface IQuery : IRequest<int> { }")]
    [Arguments("public sealed record Happened : INotification;")]
    public async Task KRX001_Handled_Or_Not_Required(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<MissingHandlerAnalyzer>(source, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task KRX005_Two_Handlers_For_One_Request(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<MissingHandlerAnalyzer>(
            """
            public sealed record Ping : IRequest<int>;
            public static partial class First { private static int Handle(Ping p) => 1; }
            public static partial class Second { private static int Handle(Ping p) => 2; }
            """,
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX005", "KRX005"]);
        await Assert.That(Messages(diagnostics, "KRX005").Distinct())
                    .IsEquivalentTo(["Request type 'Ping' has more than one handler in this assembly: 'First.Handle', 'Second.Handle'"]);
    }

    [Test]
    public async Task KRX005_Notifications_May_Have_Many_Handlers(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<MissingHandlerAnalyzer>(
            """
            public sealed record Placed : INotification;
            public static partial class First { private static ValueTask Handle(Placed p) => default; }
            public static partial class Second { private static ValueTask Handle(Placed p) => default; }
            """,
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    [Arguments("sender.Send(new Ping())")]
    [Arguments("mediator.Send(new Ping())")]
    [Arguments("sender.Stream(new Numbers())")]
    public async Task KRX021_Struct_Request_Through_ISender(string call, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<StructRequestAnalyzer>(
            $$"""
            public readonly record struct Ping : IRequest<int>;
            public readonly record struct Numbers : IStreamRequest<int>;
            public static class Caller
            {
                public static object Run(ISender sender, IMediator mediator) => {{call}};
            }
            """,
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX021"]);
    }

    [Test]
    public async Task KRX021_Class_Request_Through_ISender(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<StructRequestAnalyzer>(
            """
            public sealed record Ping : IRequest<int>;
            public static class Caller
            {
                public static ValueTask<int> Run(ISender sender) => sender.Send(new Ping());
            }
            """,
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task KRX022_Below_CSharp_14(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<LanguageVersionAnalyzer>("public sealed class Ping : IRequest<int> { }", cancellationToken, LanguageVersion.CSharp13);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX022"]);
        await Assert.That(Messages(diagnostics, "KRX022"))
                    .IsEquivalentTo(
                         [
                             "This project compiles with C# 13.0, but Keryx needs C# 14 or later; remove the LangVersion setting (the Keryx package then defaults it to 'latest') or set it to 14 or later",
                         ]
                     );
    }

    [Test]
    [Arguments(LanguageVersion.CSharp14)]
    [Arguments(LanguageVersion.Latest)]
    public async Task KRX022_CSharp_14_Or_Later(LanguageVersion languageVersion, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<LanguageVersionAnalyzer>("public sealed class Ping : IRequest<int> { }", cancellationToken, languageVersion);

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    [Arguments(LanguageVersion.CSharp13, true)]
    [Arguments(LanguageVersion.CSharp14, false)]
    [Arguments(LanguageVersion.Latest, false)]
    public async Task KRX022_In_A_NetStandard_Library(LanguageVersion languageVersion, bool reported, CancellationToken cancellationToken)
    {
        // A netstandard2.0 contracts or handler library: compiled against the netstandard2.0 Keryx.dll, where the language floor is the same.
        var compilation = CSharpCompilation.Create(
            "Sample.Contracts",
            [CSharpSyntaxTree.ParseText("public sealed class Ping : Keryx.IRequest<int> { }", new(languageVersion), cancellationToken: cancellationToken)],
            [.. Abstractions.ManifestContractTests.FrameworkReferences, Abstractions.ManifestContractTests.KeryxNetStandard],
            new(OutputKind.DynamicallyLinkedLibrary)
        );

        var diagnostics = await compilation.WithAnalyzers([new LanguageVersionAnalyzer()]).GetAnalyzerDiagnosticsAsync(cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(reported ? (string[])["KRX022"] : []);
    }

    [Test]
    public async Task KRX022_Is_The_Only_Keryx_Error_Below_CSharp_14(CancellationToken cancellationToken)
    {
        var result = await Builder
                          .WithLanguageVersion(LanguageVersion.CSharp13)
                          .WithAnalyzer<LanguageVersionAnalyzer>()
                          .WithAnalyzer<HandlerAnalyzer>()
                          .WithAnalyzer<MessageTypeAnalyzer>()
                          .WithAnalyzer<MissingHandlerAnalyzer>()
                          .WithAnalyzer<StructRequestAnalyzer>()
                          .WithAnalyzer<HostOnlyAttributeAnalyzer>()
                          .AddSources(
                               """
                               using Keryx;
                               [assembly: ExternallyHandled(typeof(Ping))]
                               public sealed class Ping : IRequest<int> { }
                               public sealed class Both : IRequest<int>, INotification { }
                               public class Handler { public string Handle(Ping request) => ""; }
                               """
                           )
                          .Build()
                          .GenerateAsync(cancellationToken);

        var ids = result.AnalyzerResults.Values.SelectMany(static r => r.Diagnostics).Select(static d => d.Id).ToArray();
        await Assert.That(ids).IsEquivalentTo(["KRX022"]);
        await Assert.That(result.Results.Values.Single().SyntaxTrees).IsEmpty();
    }

    [Test]
    [Arguments("", "ExternallyHandled(typeof(Ping))", "ExternallyHandled")]
    [Arguments("", "ExcludeHandler(typeof(Ping))", "ExcludeHandler")]
    [Arguments("", "ExcludeBehavior(typeof(Ping))", "ExcludeBehavior")]
    [Arguments("[assembly: KeryxContracts]", "ExternallyHandled(typeof(Ping))", "ExternallyHandled")]
    [Arguments("[assembly: KeryxContracts]", "ExcludeHandler(typeof(Ping))", "ExcludeHandler")]
    [Arguments("[assembly: KeryxContracts]", "ExcludeBehavior(typeof(Ping))", "ExcludeBehavior")]
    public async Task KRX043_Host_Only_Attribute_Outside_A_Host(string assemblyKind, string attribute, string name, CancellationToken cancellationToken)
    {
        // A library (no assembly attribute) and a contracts assembly: neither is a host, so the attribute has no effect (OQ-54).
        var diagnostics = await AnalyzeAsync<HostOnlyAttributeAnalyzer>(
            $"{assemblyKind}\n[assembly: {attribute}]\npublic sealed record Ping : IRequest<int>;",
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX043"]);
        await Assert.That(Messages(diagnostics, "KRX043"))
                    .IsEquivalentTo([$"[assembly: {name}] has no effect, because this assembly is not a host ([assembly: KeryxHost])"]);
        var diagnostic = diagnostics.Single();
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(diagnostic.Location.SourceTree!.GetText(cancellationToken).ToString(diagnostic.Location.SourceSpan)).IsEqualTo(attribute);
        await Assert.That(diagnostic.Descriptor.HelpLinkUri).IsEqualTo(Descriptors.HelpLinkBase + "krx043");
    }

    [Test]
    public async Task KRX043_Not_Reported_In_A_Host(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HostOnlyAttributeAnalyzer>(
            """
            [assembly: KeryxHost]
            [assembly: ExternallyHandled(typeof(Ping))]
            [assembly: ExcludeHandler(typeof(Ping))]
            [assembly: ExcludeBehavior(typeof(Ping))]
            public sealed record Ping : IRequest<int>;
            """,
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task KRX043_Reports_Each_Attribute(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HostOnlyAttributeAnalyzer>(
            """
            [assembly: ExternallyHandled(typeof(Ping))]
            [assembly: ExternallyHandled(typeof(Pong))]
            [assembly: ExcludeHandler(typeof(Ping)), ExcludeBehavior(typeof(Ping))]
            [assembly: System.Reflection.AssemblyMetadata("a", "b")]
            public sealed record Ping : IRequest<int>;
            public sealed record Pong : IRequest<int>;
            """,
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX043", "KRX043", "KRX043", "KRX043"]);
    }
}
