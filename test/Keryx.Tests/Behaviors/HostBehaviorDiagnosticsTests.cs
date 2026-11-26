using System.Globalization;
using Keryx.Analyzers;
using Keryx.Tests.Abstractions;
using Keryx.Tests.Hosts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Keryx.Tests.Behaviors;

/// <summary>
///     The host's behavior diagnostics (ADR 0001, Diagnostics): KRX035 for every global behavior it applies, KRX036 for an
///     <c>ExcludeBehavior</c> that removes nothing, and KRX008 for behavior pairs that involve a referenced behavior (in-source pairs are the
///     analyzer's).
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class HostBehaviorDiagnosticsTests : HostTest
{
    private const string Behaviors = """
        public sealed record Ping : IRequest<string>;
        public static partial class PingHandler { private static string Handle(Ping request) => ""; }

        public sealed record Placed : INotification;

        [GlobalBehavior]
        public sealed class Audit<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
        {
            public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse> =>
                next.InvokeAsync(cancellationToken);
        }

        [GlobalBehavior]
        public sealed class Trace<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
        {
            public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse> =>
                next.InvokeAsync(cancellationToken);
        }

        public sealed class Unused<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
        {
            public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse> =>
                next.InvokeAsync(cancellationToken);
        }

        public sealed class OnPublish<TNotification> : INotificationPipelineBehavior<TNotification> where TNotification : INotification
        {
            public ValueTask Handle<TNext>(TNotification notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext =>
                next.InvokeAsync(cancellationToken);
        }

        """;

    [Test]
    public async Task KRX035_And_KRX036(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(
                                           Usings
                                         + """
                                           [assembly: KeryxHost]
                                           [assembly: ExcludeBehavior(typeof(Sample.Trace<,>))]
                                           [assembly: ExcludeBehavior(typeof(Sample.Unused<,>))]
                                           [assembly: ExcludeBehavior(typeof(Sample.Audit<Sample.Placed, int>))]
                                           [assembly: ExcludeBehavior(typeof(string))]
                                           [assembly: ExcludeBehavior(typeof(Sample.OnPublish<>))]

                                           namespace Sample;

                                           """
                                         + Behaviors
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        var diagnostics = result.Results[typeof(HostGenerator)].Diagnostics;
        await Assert.That(diagnostics.Select(static d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}").Order(StringComparer.Ordinal))
                    .IsEquivalentTo(
                         [
                             // Trace is excluded entirely, so only Audit is listed; an exclusion of one closing leaves the global applied.
                             "KRX035: Global behavior 'Audit<TRequest, TResponse>' from assembly 'TestProject' applies in this host",
                             "KRX036: [assembly: ExcludeBehavior(typeof(Audit<Placed, int>))] has no effect, because 'Audit<Placed, int>' is not part of any pipeline in this host",
                             "KRX036: [assembly: ExcludeBehavior(typeof(Unused<,>))] has no effect, because 'Unused<,>' is not part of any pipeline in this host",
                             "KRX036: [assembly: ExcludeBehavior(typeof(string))] has no effect, because 'string' is not part of any pipeline in this host",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );

        // Reported where the user wrote them: KRX035 on [GlobalBehavior], KRX036 on the exclusion.
        foreach (var diagnostic in diagnostics)
        {
            var text = diagnostic.Location.SourceTree!.GetText(cancellationToken).ToString(diagnostic.Location.SourceSpan);
            await Assert.That(text).StartsWith(diagnostic.Id == "KRX035" ? "GlobalBehavior" : "ExcludeBehavior");
        }
    }

    [Test]
    public async Task KRX036_Is_Not_Reported_For_An_Exclusion_That_Removes_A_Behavior(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + "[assembly: KeryxHost]\n[assembly: ExcludeBehavior(typeof(Sample.Audit<Sample.Ping, string>))]\nnamespace Sample;\n" + Behaviors)
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(result.Results[typeof(HostGenerator)].Diagnostics.Select(static d => d.Id).Order(StringComparer.Ordinal))
                    .IsEquivalentTo(["KRX035", "KRX035"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    [MethodDataSource(nameof(Forms))]
    public async Task KRX008_For_A_Pair_With_A_Referenced_Behavior(OrderingForm form, CancellationToken cancellationToken)
    {
        // A library behavior in Phase.Transaction; the host's own behavior, in Phase.Observability, claims to run after it.
        var library = Library(
            """
            namespace Lib;

            [global::Keryx.BehaviorOrder(global::Keryx.Phase.Transaction)]
            public sealed class Transaction<TRequest, TResponse> : global::Keryx.IRequestPipelineBehavior<TRequest, TResponse>
                where TRequest : global::Keryx.IRequest<TResponse>
            {
                public global::System.Threading.Tasks.ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, global::System.Threading.CancellationToken cancellationToken)
                    where TNext : struct, global::Keryx.INext<TResponse> => next.InvokeAsync(cancellationToken);
            }

            public sealed class Cycle1<TRequest, TResponse> : global::Keryx.IRequestPipelineBehavior<TRequest, TResponse>
                where TRequest : global::Keryx.IRequest<TResponse>
            {
                public global::System.Threading.Tasks.ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, global::System.Threading.CancellationToken cancellationToken)
                    where TNext : struct, global::Keryx.INext<TResponse> => next.InvokeAsync(cancellationToken);
            }
            """
        );
        var source = OrderingForms.Expand(
            """
            [assembly: KeryxHost]

            namespace Sample;

            public sealed record Ping : IRequest<string>;
            public sealed record Pong : IRequest<string>;

            [BehaviorOrder(Phase.Observability)]
            {{After:Lib.Transaction<Sample.Ping, string>}}
            public sealed class Early : IRequestPipelineBehavior<Ping, string>
            {
                public ValueTask<string> Handle<TNext>(Ping request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<string> =>
                    next.InvokeAsync(cancellationToken);
            }

            {{Before:Lib.Cycle1<Sample.Pong, string>}}
            {{After:Lib.Cycle1<Sample.Pong, string>}}
            public sealed class Both : IRequestPipelineBehavior<Pong, string>
            {
                public ValueTask<string> Handle<TNext>(Pong request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<string> =>
                    next.InvokeAsync(cancellationToken);
            }

            public static partial class PingHandler
            {
                [Behavior(typeof(Early))]
                [Behavior(typeof(Lib.Transaction<,>))]
                private static string Handle(Ping request) => "";
            }

            public static partial class PongHandler
            {
                [Behavior(typeof(Both))]
                [Behavior(typeof(Lib.Cycle1<,>))]
                private static string Handle(Pong request) => "";
            }
            """,
            form
        );

        var result = await HostBuilder.AddReferences(library).AddSources(Usings + source).Build().GenerateAsync(cancellationToken);

        await Assert.That(result.Results[typeof(HostGenerator)].Diagnostics.Select(static d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}").Order(StringComparer.Ordinal))
                    .IsEquivalentTo(
                         [
                             "KRX008: 'Lib.Cycle1<Sample.Pong, string>' is part of a RunsAfter/RunsBefore cycle between 'Lib.Cycle1<Sample.Pong, string>', 'Sample.Both', so no order satisfies them",
                             "KRX008: 'Sample.Early' declares RunsAfter 'Lib.Transaction<Sample.Ping, string>', but its position (Phase.Observability, order 0) puts it before (outside) 'Lib.Transaction<Sample.Ping, string>' (Phase.Transaction, order 0); RunsAfter and RunsBefore only order behaviors that have the same phase and order",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
    }

    [Test]
    [MethodDataSource(nameof(Forms))]
    public async Task KRX008_Is_Checked_Once_Per_Pair_Declared_From_Both_Sides(OrderingForm form, CancellationToken cancellationToken)
    {
        // Two referenced behaviors declare the same contradiction from both sides: A runs after B, and B runs before A. The filler edge before
        // each significant one puts both forms on one class under Mixed.
        var library = Library(
            OrderingForms.Expand(
                """
                using Keryx;

                namespace Lib;

                public sealed record Ping : IRequest<string>;

                [BehaviorOrder(Phase.Observability)]
                {{After:string}}
                {{After:B}}
                public sealed class A : IRequestPipelineBehavior<Ping, string>
                {
                    public global::System.Threading.Tasks.ValueTask<string> Handle<TNext>(Ping request, TNext next, global::System.Threading.CancellationToken cancellationToken)
                        where TNext : struct, INext<string> => next.InvokeAsync(cancellationToken);
                }

                [BehaviorOrder(Phase.Transaction)]
                {{After:string}}
                {{Before:A}}
                public sealed class B : IRequestPipelineBehavior<Ping, string>
                {
                    public global::System.Threading.Tasks.ValueTask<string> Handle<TNext>(Ping request, TNext next, global::System.Threading.CancellationToken cancellationToken)
                        where TNext : struct, INext<string> => next.InvokeAsync(cancellationToken);
                }
                """,
                form
            )
        );
        var source = """
            [assembly: KeryxHost]

            namespace Sample;

            public static partial class PingHandler
            {
                [Behavior(typeof(Lib.A))]
                [Behavior(typeof(Lib.B))]
                private static string Handle(Lib.Ping request) => "";
            }
            """;

        var result = await HostBuilder.AddReferences(library).AddSources(Usings + source).Build().GenerateAsync(cancellationToken);

        await Assert.That(result.Results[typeof(HostGenerator)].Diagnostics.Select(static d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}"))
                    .IsEquivalentTo(
                         [
                             "KRX008: 'Lib.A' declares RunsAfter 'Lib.B', but its position (Phase.Observability, order 0) puts it before (outside) 'Lib.B' (Phase.Transaction, order 0); RunsAfter and RunsBefore only order behaviors that have the same phase and order",
                         ]
                     );
    }

    [Test]
    public async Task KRX008_A_Cycle_Is_Reported_Once_However_Many_Pipelines_Contain_It(CancellationToken cancellationToken)
    {
        // An open in-source behavior in a cycle with an open referenced one, both attached to two handlers: two pipelines, two closings of
        // the same cycle, one KRX008. The edges name an open behavior, which only the typeof form can (C# has no RunsAfter<X<,>>).
        var source = """
            [assembly: KeryxHost]

            namespace Sample;

            public sealed record Ping : IRequest<string>;
            public sealed record Pong : IRequest<string>;

            [RunsBefore(typeof(Lib.Cycle1<,>))]
            [RunsAfter(typeof(Lib.Cycle1<,>))]
            public sealed class Both<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
            {
                public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse> =>
                    next.InvokeAsync(cancellationToken);
            }

            public static partial class PingHandler
            {
                [Behavior(typeof(Both<,>))]
                [Behavior(typeof(Lib.Cycle1<,>))]
                private static string Handle(Ping request) => "";
            }

            public static partial class PongHandler
            {
                [Behavior(typeof(Both<,>))]
                [Behavior(typeof(Lib.Cycle1<,>))]
                private static string Handle(Pong request) => "";
            }
            """;

        var result = await HostBuilder.AddReferences(Library(CycleLibrary)).AddSources(Usings + source).Build().GenerateAsync(cancellationToken);

        var diagnostics = result.Results[typeof(HostGenerator)].Diagnostics;
        await Assert.That(diagnostics.Select(static d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}"))
                    .IsEquivalentTo(
                         [
                             "KRX008: 'Lib.Cycle1<Sample.Ping, string>' is part of a RunsAfter/RunsBefore cycle between 'Lib.Cycle1<Sample.Ping, string>', 'Sample.Both<Sample.Ping, string>', so no order satisfies them",
                         ]
                     );
        var diagnostic = diagnostics.Single();
        await Assert.That(diagnostic.Location.SourceTree!.GetText(cancellationToken).ToString(diagnostic.Location.SourceSpan)).IsEqualTo("RunsBefore(typeof(Lib.Cycle1<,>))");
    }

    [Test]
    [MethodDataSource(nameof(Forms))]
    public async Task KRX008_An_In_Source_Cycle_With_A_Referenced_Behavior_Downstream_Is_The_Analyzers(OrderingForm form, CancellationToken cancellationToken)
    {
        // A and B (in source) form a cycle; A also runs before a referenced behavior in the same bucket, which waits on the cycle without
        // being part of it. The host reports nothing; the analyzer reports the cycle once.
        var source = OrderingForms.Expand(
            """
            [assembly: KeryxHost]

            namespace Sample;

            public sealed record Ping : IRequest<string>;

            {{After:string}}
            {{After:B}}
            {{Before:Lib.Cycle1<Sample.Ping, string>}}
            public sealed class A : IRequestPipelineBehavior<Ping, string>
            {
                public ValueTask<string> Handle<TNext>(Ping request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<string> =>
                    next.InvokeAsync(cancellationToken);
            }

            {{After:string}}
            {{After:A}}
            public sealed class B : IRequestPipelineBehavior<Ping, string>
            {
                public ValueTask<string> Handle<TNext>(Ping request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<string> =>
                    next.InvokeAsync(cancellationToken);
            }

            public static partial class PingHandler
            {
                [Behavior(typeof(A))]
                [Behavior(typeof(B))]
                [Behavior(typeof(Lib.Cycle1<,>))]
                private static string Handle(Ping request) => "";
            }
            """,
            form
        );

        var result = await HostBuilder.WithAnalyzer<Analyzers.Diagnostics.BehaviorAnalyzer>()
                                      .AddReferences(Library(CycleLibrary))
                                      .AddSources(Usings + source)
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        await Assert.That(result.Results[typeof(HostGenerator)].Diagnostics).IsEmpty();
        await Assert.That(
                         result.AnalyzerResults[typeof(Analyzers.Diagnostics.BehaviorAnalyzer)]
                               .Diagnostics.Select(static d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}")
                     )
                    .IsEquivalentTo(["KRX008: 'A' is part of a RunsAfter/RunsBefore cycle between 'A', 'B', so no order satisfies them"]);
    }

    private const string CycleLibrary = """
        namespace Lib;

        public sealed class Cycle1<TRequest, TResponse> : global::Keryx.IRequestPipelineBehavior<TRequest, TResponse>
            where TRequest : global::Keryx.IRequest<TResponse>
        {
            public global::System.Threading.Tasks.ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, global::System.Threading.CancellationToken cancellationToken)
                where TNext : struct, global::Keryx.INext<TResponse> => next.InvokeAsync(cancellationToken);
        }
        """;

    public static IEnumerable<Func<OrderingForm>> Forms() => [() => OrderingForm.TypeOf, () => OrderingForm.Generic, () => OrderingForm.Mixed];

    /// <summary>Compiles a referenced library against Keryx.</summary>
    private static MetadataReference Library(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Lib",
            [CSharpSyntaxTree.ParseText(source, new(LanguageVersion.Latest))],
            [.. ManifestContractTests.FrameworkReferences, MetadataReference.CreateFromFile(typeof(Unit).Assembly.Location)],
            new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
        );
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        return emit.Success
            ? MetadataReference.CreateFromImage(image.ToArray())
            : throw new InvalidOperationException(string.Join(Environment.NewLine, emit.Diagnostics.Select(static d => d.ToString())));
    }
}
