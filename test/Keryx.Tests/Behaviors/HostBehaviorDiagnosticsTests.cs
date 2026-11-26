using System.Globalization;
using Keryx.Analyzers;
using Keryx.Tests.Hosts;
using Microsoft.CodeAnalysis;

namespace Keryx.Tests.Behaviors;

/// <summary>
///     The host's behavior diagnostics (ADR 0001, Diagnostics): KRX035 for every global behavior it applies (marked on the class or only on a
///     behavior method), KRX036 for an <c>ExcludeBehavior</c> that removes nothing, and KRX008 for behavior pairs that involve a referenced
///     behavior (in-source pairs are the analyzer's), per kind where a behavior method overrides its class's ordering.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class HostBehaviorDiagnosticsTests : HostTest
{
    private const string Behaviors = """
        public sealed record Ping : IRequest<string>;
        public static partial class PingHandler { private static string Handle(Ping request) => ""; }

        public sealed record Placed : INotification;

        [RequestBehavior]
        [GlobalBehavior]
        public static class Audit
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse> =>
                next.InvokeAsync(cancellationToken);
        }

        [RequestBehavior]
        [GlobalBehavior]
        public static class Trace
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse> =>
                next.InvokeAsync(cancellationToken);
        }

        // Global only through its method.
        [RequestBehavior]
        public static class MethodGlobal
        {
            [GlobalBehavior]
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse> =>
                next.InvokeAsync(cancellationToken);
        }

        [RequestBehavior]
        public static class Unused
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse> =>
                next.InvokeAsync(cancellationToken);
        }

        [NotificationBehavior]
        public static class OnPublish
        {
            public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, CancellationToken cancellationToken)
                where TNotification : INotification
                where TNext : struct, INext =>
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
                                           [assembly: ExcludeBehavior(typeof(Sample.Trace))]
                                           [assembly: ExcludeBehavior(typeof(Sample.Unused))]
                                           [assembly: ExcludeBehavior(typeof(string))]
                                           [assembly: ExcludeBehavior(typeof(Sample.OnPublish))]

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
                             // Trace is excluded, so only Audit and the global marked only on its method are listed.
                             "KRX035: Global request behavior 'Audit' from assembly 'TestProject' applies in this host",
                             "KRX035: Global request behavior 'MethodGlobal' from assembly 'TestProject' applies in this host",
                             "KRX036: [assembly: ExcludeBehavior(typeof(OnPublish))] has no effect, because 'OnPublish' is not part of any pipeline in this host",
                             "KRX036: [assembly: ExcludeBehavior(typeof(Unused))] has no effect, because 'Unused' is not part of any pipeline in this host",
                             "KRX036: [assembly: ExcludeBehavior(typeof(string))] has no effect, because 'string' is not part of any pipeline in this host",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );

        // Reported where the user wrote them: KRX035 on [GlobalBehavior] (the method's, for a global marked only there), KRX036 on the
        // exclusion.
        foreach (var diagnostic in diagnostics)
        {
            var text = diagnostic.Location.SourceTree!.GetText(cancellationToken).ToString(diagnostic.Location.SourceSpan);
            await Assert.That(text).StartsWith(diagnostic.Id == "KRX035" ? "GlobalBehavior" : "ExcludeBehavior");
        }
    }

    [Test]
    public async Task KRX035_Lists_A_Global_Behavior_Once_Per_Kind(CancellationToken cancellationToken)
    {
        // OQ-57: one class global for all three kinds is listed once per kind, each on the class's [GlobalBehavior].
        const string source = """
            [assembly: KeryxHost]

            namespace Sample;

            public sealed record Ping : IRequest<string>;
            public static partial class PingHandler { private static string Handle(Ping request) => ""; }

            public sealed record Numbers : IStreamRequest<int>;
            public static partial class NumbersHandler
            {
                private static async IAsyncEnumerable<int> Handle(Numbers request)
                {
                    await Task.Yield();
                    yield return 1;
                }
            }

            public sealed record Placed : INotification;
            public static partial class PlacedHandler { private static ValueTask Handle(Placed notification) => default; }

            [RequestBehavior]
            [StreamBehavior]
            [NotificationBehavior]
            [GlobalBehavior]
            public static class Everywhere
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                    where TRequest : IRequest<TResponse>
                    where TNext : struct, INext<TResponse> =>
                    next.InvokeAsync(cancellationToken);

                public static IAsyncEnumerable<TItem> HandleAsync<TRequest, TItem, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                    where TRequest : IStreamRequest<TItem>
                    where TNext : struct, IStreamNext<TItem> =>
                    next.InvokeAsync(cancellationToken);

                public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, CancellationToken cancellationToken)
                    where TNotification : INotification
                    where TNext : struct, INext =>
                    next.InvokeAsync(cancellationToken);
            }
            """;
        var result = await HostBuilder.AddSources(Usings + source).Build().GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        var diagnostics = result.Results[typeof(HostGenerator)].Diagnostics;
        await Assert.That(diagnostics.Select(static d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}").Order(StringComparer.Ordinal))
                    .IsEquivalentTo(
                         [
                             "KRX035: Global notification behavior 'Everywhere' from assembly 'TestProject' applies in this host",
                             "KRX035: Global request behavior 'Everywhere' from assembly 'TestProject' applies in this host",
                             "KRX035: Global stream behavior 'Everywhere' from assembly 'TestProject' applies in this host",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );

        // Each on the class's [GlobalBehavior]: the attribute that directly precedes the class declaration.
        foreach (var diagnostic in diagnostics)
        {
            var tree = diagnostic.Location.SourceTree!.GetText(cancellationToken).ToString();
            var span = diagnostic.Location.SourceSpan;
            await Assert.That(tree.Substring(span.Start, span.Length)).StartsWith("GlobalBehavior");
            await Assert.That(tree[span.End..].TrimStart().StartsWith(']')).IsTrue();
            await Assert.That(tree[span.End..].TrimStart()[1..].TrimStart().StartsWith("public static class Everywhere", StringComparison.Ordinal)).IsTrue();
        }
    }

    [Test]
    public async Task KRX036_Is_Not_Reported_For_An_Exclusion_That_Removes_A_Behavior(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + "[assembly: KeryxHost]\n[assembly: ExcludeBehavior(typeof(Sample.Audit))]\nnamespace Sample;\n" + Behaviors)
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(result.Results[typeof(HostGenerator)].Diagnostics.Select(static d => d.GetMessage(CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal))
                    .IsEquivalentTo(
                         [
                             "Global request behavior 'MethodGlobal' from assembly 'TestProject' applies in this host",
                             "Global request behavior 'Trace' from assembly 'TestProject' applies in this host",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
    }

    [Test]
    public async Task ExcludeBehavior_Removes_A_Class_From_Pipelines_Of_Every_Kind(CancellationToken cancellationToken)
    {
        // One exclusion names the class: its request, stream and notification behaviors all leave every pipeline, and the exclusion counts
        // as used (no KRX036).
        const string source = """
            [assembly: KeryxHost]
            [assembly: ExcludeBehavior(typeof(Sample.Everywhere))]

            namespace Sample;

            public sealed record Ping : IRequest<string>;
            public sealed record Names : IStreamRequest<string>;
            public sealed record Placed : INotification;
            public static partial class PingHandler { private static string Handle(Ping request) => ""; }
            public static partial class NamesHandler { private static IAsyncEnumerable<string> Handle(Names request) => null!; }
            public static partial class PlacedHandler { private static ValueTask Handle(Placed notification) => default; }

            [RequestBehavior]
            [StreamBehavior]
            [NotificationBehavior]
            [GlobalBehavior]
            public static class Everywhere
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next)
                    where TRequest : IRequest<TResponse>
                    where TNext : struct, INext<TResponse> => next.InvokeAsync(default);

                public static IAsyncEnumerable<TItem> HandleAsync<TRequest, TItem, TNext>(TRequest request, TNext next)
                    where TRequest : IStreamRequest<TItem>
                    where TNext : struct, IStreamNext<TItem> => next.InvokeAsync(default);

                public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next)
                    where TNotification : INotification
                    where TNext : struct, INext => next.InvokeAsync(default);
            }
            """;

        var excluded = await HostBuilder.AddSources(Usings + source).Build().GenerateAsync(cancellationToken);
        var applied = await HostBuilder.WithProjectName("Applied")
                                       .AddSources(Usings + source.Replace("[assembly: ExcludeBehavior(typeof(Sample.Everywhere))]", "", StringComparison.Ordinal))
                                       .Build()
                                       .GenerateAsync(cancellationToken);

        excluded.AssertCompilationWasSuccessful();
        await Assert.That(excluded.Results[typeof(HostGenerator)].Diagnostics).IsEmpty();
        await Assert.That(PipelineGeneratorTests.Source(excluded, Analyzers.Hosts.HostEmitter.MediatorHintName)).DoesNotContain("Everywhere");
        var mediator = PipelineGeneratorTests.Source(applied, Analyzers.Hosts.HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("global::Sample.Everywhere.Handle<global::Sample.Ping, string, ");
        await Assert.That(mediator).Contains("global::Sample.Everywhere.HandleAsync<global::Sample.Names, string, ");
        await Assert.That(mediator).Contains("global::Sample.Everywhere.Handle<global::Sample.Placed, ");
    }

    [Test]
    public async Task KRX008_For_A_Pair_With_A_Referenced_Behavior(CancellationToken cancellationToken)
    {
        // A library behavior in Phase.Transaction; the host's own behavior, in Phase.Observability, claims to run after it.
        var library = Library(
            """
            using Keryx;

            namespace Lib;

            [RequestBehavior]
            [BehaviorOrder(Phase.Transaction)]
            public static class Transaction
            {
                public static global::System.Threading.Tasks.ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next)
                    where TRequest : IRequest<TResponse>
                    where TNext : struct, INext<TResponse> => next.InvokeAsync(default);
            }

            [RequestBehavior]
            public static class Cycle1
            {
                public static global::System.Threading.Tasks.ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next)
                    where TRequest : IRequest<TResponse>
                    where TNext : struct, INext<TResponse> => next.InvokeAsync(default);
            }
            """
        );
        const string source = """
            [assembly: KeryxHost]

            namespace Sample;

            public sealed record Ping : IRequest<string>;
            public sealed record Pong : IRequest<string>;

            [RequestBehavior]
            [BehaviorOrder(Phase.Observability)]
            [RunsAfter(typeof(Lib.Transaction))]
            public static class Early
            {
                public static ValueTask<string> Handle<TNext>(Ping request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<string> =>
                    next.InvokeAsync(cancellationToken);
            }

            [RequestBehavior]
            [RunsBefore(typeof(Lib.Cycle1))]
            [RunsAfter(typeof(Lib.Cycle1))]
            public static class Both
            {
                public static ValueTask<string> Handle<TNext>(Pong request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<string> =>
                    next.InvokeAsync(cancellationToken);
            }

            public static partial class PingHandler
            {
                [Behavior(typeof(Early))]
                [Behavior(typeof(Lib.Transaction))]
                private static string Handle(Ping request) => "";
            }

            public static partial class PongHandler
            {
                [Behavior(typeof(Both))]
                [Behavior(typeof(Lib.Cycle1))]
                private static string Handle(Pong request) => "";
            }
            """;

        var result = await HostBuilder.AddReferences(library).AddSources(Usings + source).Build().GenerateAsync(cancellationToken);

        await Assert.That(result.Results[typeof(HostGenerator)].Diagnostics.Select(static d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}").Order(StringComparer.Ordinal))
                    .IsEquivalentTo(
                         [
                             "KRX008: 'Lib.Cycle1' is part of a RunsAfter/RunsBefore cycle between 'Lib.Cycle1', 'Sample.Both', so no order satisfies them",
                             "KRX008: 'Sample.Early' declares RunsAfter 'Lib.Transaction', but its position (Phase.Observability, order 0) puts it before (outside) 'Lib.Transaction' (Phase.Transaction, order 0); RunsAfter and RunsBefore only order behaviors that have the same phase and order",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
    }

    [Test]
    public async Task KRX008_Per_Kind_When_A_Method_Overrides_The_Class(CancellationToken cancellationToken)
    {
        // Early's class edge contradicts its phase in the request pipeline; its stream method's own [RunsAfter] list does too, in the stream
        // pipeline: one KRX008 per kind.
        var library = Library(
            """
            using Keryx;

            namespace Lib;

            [RequestBehavior]
            [StreamBehavior]
            [BehaviorOrder(Phase.Transaction)]
            public static class Transaction
            {
                public static global::System.Threading.Tasks.ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next)
                    where TRequest : IRequest<TResponse>
                    where TNext : struct, INext<TResponse> => next.InvokeAsync(default);

                public static global::System.Collections.Generic.IAsyncEnumerable<TItem> HandleAsync<TRequest, TItem, TNext>(TRequest request, TNext next)
                    where TRequest : IStreamRequest<TItem>
                    where TNext : struct, IStreamNext<TItem> => next.InvokeAsync(default);
            }
            """
        );
        const string source = """
            [assembly: KeryxHost]

            namespace Sample;

            public sealed record Ping : IRequest<string>;
            public sealed record Names : IStreamRequest<string>;

            [RequestBehavior]
            [StreamBehavior]
            [BehaviorOrder(Phase.Observability)]
            [RunsAfter(typeof(Lib.Transaction))]
            public static class Early
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next)
                    where TRequest : IRequest<TResponse>
                    where TNext : struct, INext<TResponse> => next.InvokeAsync(default);

                [RunsAfter(typeof(Lib.Transaction))]
                public static IAsyncEnumerable<TItem> HandleAsync<TRequest, TItem, TNext>(TRequest request, TNext next)
                    where TRequest : IStreamRequest<TItem>
                    where TNext : struct, IStreamNext<TItem> => next.InvokeAsync(default);
            }

            public static partial class PingHandler
            {
                [Behavior(typeof(Early))]
                [Behavior(typeof(Lib.Transaction))]
                private static string Handle(Ping request) => "";
            }

            public static partial class NamesHandler
            {
                [Behavior(typeof(Early))]
                [Behavior(typeof(Lib.Transaction))]
                private static IAsyncEnumerable<string> Handle(Names request) => null!;
            }
            """;

        var result = await HostBuilder.AddReferences(library).AddSources(Usings + source).Build().GenerateAsync(cancellationToken);

        await Assert.That(result.Results[typeof(HostGenerator)].Diagnostics.Select(static d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}").Order(StringComparer.Ordinal))
                    .IsEquivalentTo(
                         [
                             "KRX008: 'Sample.Early' declares RunsAfter 'Lib.Transaction' for its stream behavior, but its position (Phase.Observability, order 0) puts it before (outside) 'Lib.Transaction' (Phase.Transaction, order 0); RunsAfter and RunsBefore only order behaviors that have the same phase and order",
                             "KRX008: 'Sample.Early' declares RunsAfter 'Lib.Transaction', but its position (Phase.Observability, order 0) puts it before (outside) 'Lib.Transaction' (Phase.Transaction, order 0); RunsAfter and RunsBefore only order behaviors that have the same phase and order",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
    }

    [Test]
    public async Task KRX008_For_Notification_Behavior_Pairs_With_A_Referenced_Behavior(CancellationToken cancellationToken)
    {
        // A library notification behavior in Phase.Transaction; the host's own, in Phase.Observability, claims to run after it.
        var library = Library(
            """
            using Keryx;

            namespace Lib;

            [NotificationBehavior]
            [BehaviorOrder(Phase.Transaction)]
            public static class PublishTransaction
            {
                public static global::System.Threading.Tasks.ValueTask Handle<T, TNext>(T notification, TNext next)
                    where T : INotification
                    where TNext : struct, INext => next.InvokeAsync(default);
            }
            """
        );
        const string source = """
            [assembly: KeryxHost]

            namespace Sample;

            [NotificationBehavior]
            [BehaviorOrder(Phase.Observability)]
            [RunsAfter(typeof(Lib.PublishTransaction))]
            public static class EarlyPublish
            {
                public static ValueTask Handle<TNext>(Placed notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext =>
                    next.InvokeAsync(cancellationToken);
            }

            [Behavior(typeof(EarlyPublish))]
            [Behavior(typeof(Lib.PublishTransaction))]
            public sealed record Placed : INotification;

            public static partial class OnPlaced
            {
                private static ValueTask Handle(Placed notification) => default;
            }
            """;

        var result = await HostBuilder.AddReferences(library).AddSources(Usings + source).Build().GenerateAsync(cancellationToken);

        var diagnostics = result.Results[typeof(HostGenerator)].Diagnostics;
        await Assert.That(diagnostics.Select(static d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}"))
                    .IsEquivalentTo(
                         [
                             "KRX008: 'Sample.EarlyPublish' declares RunsAfter 'Lib.PublishTransaction', but its position (Phase.Observability, order 0) puts it before (outside) 'Lib.PublishTransaction' (Phase.Transaction, order 0); RunsAfter and RunsBefore only order behaviors that have the same phase and order",
                         ]
                     );

        // Reported at the edge.
        foreach (var diagnostic in diagnostics)
        {
            await Assert.That(diagnostic.Location.SourceTree!.GetText(cancellationToken).ToString(diagnostic.Location.SourceSpan)).StartsWith("RunsAfter");
        }
    }

    [Test]
    public async Task KRX008_Is_Checked_Once_Per_Pair_Declared_From_Both_Sides(CancellationToken cancellationToken)
    {
        // Two referenced behaviors declare the same contradiction from both sides: A runs after B, and B runs before A.
        var library = Library(
            """
            using Keryx;

            namespace Lib;

            public sealed record Ping : IRequest<string>;

            [RequestBehavior]
            [BehaviorOrder(Phase.Observability)]
            [RunsAfter(typeof(string))]
            [RunsAfter(typeof(B))]
            public static class A
            {
                public static global::System.Threading.Tasks.ValueTask<string> Handle<TNext>(Ping request, TNext next) where TNext : struct, INext<string> => next.InvokeAsync(default);
            }

            [RequestBehavior]
            [BehaviorOrder(Phase.Transaction)]
            [RunsBefore(typeof(A))]
            public static class B
            {
                public static global::System.Threading.Tasks.ValueTask<string> Handle<TNext>(Ping request, TNext next) where TNext : struct, INext<string> => next.InvokeAsync(default);
            }
            """
        );
        const string source = """
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
        // An open in-source behavior in a cycle with an open referenced one, both attached to two handlers: two pipelines, one KRX008.
        const string source = """
            [assembly: KeryxHost]

            namespace Sample;

            public sealed record Ping : IRequest<string>;
            public sealed record Pong : IRequest<string>;

            [RequestBehavior]
            [RunsBefore(typeof(Lib.Cycle1))]
            [RunsAfter(typeof(Lib.Cycle1))]
            public static class Both
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                    where TRequest : IRequest<TResponse>
                    where TNext : struct, INext<TResponse> =>
                    next.InvokeAsync(cancellationToken);
            }

            public static partial class PingHandler
            {
                [Behavior(typeof(Both))]
                [Behavior(typeof(Lib.Cycle1))]
                private static string Handle(Ping request) => "";
            }

            public static partial class PongHandler
            {
                [Behavior(typeof(Both))]
                [Behavior(typeof(Lib.Cycle1))]
                private static string Handle(Pong request) => "";
            }
            """;

        var result = await HostBuilder.AddReferences(Library(CycleLibrary)).AddSources(Usings + source).Build().GenerateAsync(cancellationToken);

        var diagnostics = result.Results[typeof(HostGenerator)].Diagnostics;
        await Assert.That(diagnostics.Select(static d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}"))
                    .IsEquivalentTo(["KRX008: 'Lib.Cycle1' is part of a RunsAfter/RunsBefore cycle between 'Lib.Cycle1', 'Sample.Both', so no order satisfies them"]);
        var diagnostic = diagnostics.Single();
        await Assert.That(diagnostic.Location.SourceTree!.GetText(cancellationToken).ToString(diagnostic.Location.SourceSpan)).IsEqualTo("RunsBefore(typeof(Lib.Cycle1))");
    }

    [Test]
    public async Task KRX008_An_In_Source_Cycle_With_A_Referenced_Behavior_Downstream_Is_The_Analyzers(CancellationToken cancellationToken)
    {
        // A and B (in source) form a cycle; A also runs before a referenced behavior in the same bucket, which waits on the cycle without
        // being part of it. The host reports nothing; the analyzer reports the cycle once.
        const string source = """
            [assembly: KeryxHost]

            namespace Sample;

            public sealed record Ping : IRequest<string>;

            [RequestBehavior]
            [RunsAfter(typeof(B))]
            [RunsBefore(typeof(Lib.Cycle1))]
            public static class A
            {
                public static ValueTask<string> Handle<TNext>(Ping request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<string> =>
                    next.InvokeAsync(cancellationToken);
            }

            [RequestBehavior]
            [RunsAfter(typeof(A))]
            public static class B
            {
                public static ValueTask<string> Handle<TNext>(Ping request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<string> =>
                    next.InvokeAsync(cancellationToken);
            }

            public static partial class PingHandler
            {
                [Behavior(typeof(A))]
                [Behavior(typeof(B))]
                [Behavior(typeof(Lib.Cycle1))]
                private static string Handle(Ping request) => "";
            }
            """;

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
        using Keryx;

        namespace Lib;

        [RequestBehavior]
        public static class Cycle1
        {
            public static global::System.Threading.Tasks.ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse> => next.InvokeAsync(default);
        }
        """;

    /// <summary>Compiles a referenced library against Keryx.</summary>
    private static MetadataReference Library(string source) => TestLibraries.Reference("Lib", source);
}
