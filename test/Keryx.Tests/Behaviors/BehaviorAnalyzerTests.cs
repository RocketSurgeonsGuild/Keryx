using System.Collections.Immutable;
using System.Globalization;
using Keryx.Analyzers.Diagnostics;
using Keryx.Tests.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Keryx.Tests.Behaviors;

/// <summary>
///     KRX008, KRX009, KRX013, KRX016, KRX018, KRX019 and KRX042 on static behaviors, each with positive and negative cases, and KRX020
///     retired.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class BehaviorAnalyzerTests : AnalyzerTest
{
    private const string Requests = """
        public sealed record Ping : IRequest<string>;
        public sealed record Count : IRequest<int>;
        public sealed record Names : IStreamRequest<string>;
        public sealed record Placed : INotification;
        public interface ICommand { }
        public interface IAudit { }

        """;

    /// <summary>An open request behavior named <paramref name="name" />, after its class attributes, with its method's attributes.</summary>
    private static string Behavior(string name, string attributes = "", string methodAttributes = "") =>
        $$"""
          [RequestBehavior]
          {{attributes}}
          public static class {{name}}
          {
              {{methodAttributes}}
              public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                  where TRequest : IRequest<TResponse>
                  where TNext : struct, INext<TResponse> =>
                  next.InvokeAsync(cancellationToken);
          }

          """;

    /// <summary>A behavior with a request (<c>Handle</c>) and a stream (<c>HandleAsync</c>) behavior method.</summary>
    private static string TwoKinds(string name, string attributes = "", string requestAttributes = "", string streamAttributes = "") =>
        $$"""
          [RequestBehavior]
          [StreamBehavior]
          {{attributes}}
          public static class {{name}}
          {
              {{requestAttributes}}
              public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                  where TRequest : IRequest<TResponse>
                  where TNext : struct, INext<TResponse> =>
                  next.InvokeAsync(cancellationToken);

              {{streamAttributes}}
              public static IAsyncEnumerable<TItem> HandleAsync<TRequest, TItem, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                  where TRequest : IStreamRequest<TItem>
                  where TNext : struct, IStreamNext<TItem> =>
                  next.InvokeAsync(cancellationToken);
          }

          """;

    // KRX008 -----------------------------------------------------------------------------------------------------------------------------

    [Test]
    public async Task KRX008_RunsAfter_Contradicts_Phase(CancellationToken cancellationToken)
    {
        var diagnostics = await Krx008Async(
            Behavior("Inner", "[BehaviorOrder(Phase.Observability)]\n[RunsAfter(typeof(string))]\n[RunsAfter(typeof(Outer))]") + Behavior("Outer", "[BehaviorOrder(Phase.Transaction)]"),
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX008"))
                    .IsEquivalentTo(
                         [
                             "'Inner' declares RunsAfter 'Outer', but its position (Phase.Observability, order 0) puts it before (outside) 'Outer' (Phase.Transaction, order 0); RunsAfter and RunsBefore only order behaviors that have the same phase and order",
                         ]
                     );
    }

    [Test]
    public async Task KRX008_RunsBefore_Contradicts_Phase(CancellationToken cancellationToken)
    {
        var diagnostics = await Krx008Async(
            Behavior("Outer", "[BehaviorOrder(Phase.Transaction)]\n[RunsBefore(typeof(Inner))]") + Behavior("Inner", "[BehaviorOrder(Phase.Observability)]"),
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX008"))
                    .IsEquivalentTo(
                         [
                             "'Outer' declares RunsBefore 'Inner', but its position (Phase.Transaction, order 0) puts it after (inside) 'Inner' (Phase.Observability, order 0); RunsAfter and RunsBefore only order behaviors that have the same phase and order",
                         ]
                     );
    }

    [Test]
    public async Task KRX008_Edge_Contradicts_Order(CancellationToken cancellationToken)
    {
        var diagnostics = await Krx008Async(
            Behavior("First", "[BehaviorOrder(Phase.Default, Order = 1)]\n[RunsBefore(typeof(Second))]") + Behavior("Second", "[BehaviorOrder(Phase.Default, Order = 0)]"),
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX008"))
                    .IsEquivalentTo(
                         [
                             "'First' declares RunsBefore 'Second', but its position (Phase.Default, order 1) puts it after (inside) 'Second' (Phase.Default, order 0); RunsAfter and RunsBefore only order behaviors that have the same phase and order",
                         ]
                     );
    }

    [Test]
    public async Task KRX008_Is_Checked_Once_Per_Pair(CancellationToken cancellationToken)
    {
        // The same contradiction declared from both sides, by classes that share two kinds: one diagnostic.
        var diagnostics = await Krx008Async(
            TwoKinds("Inner", "[BehaviorOrder(Phase.Observability)]\n[RunsAfter(typeof(Outer))]")
          + TwoKinds("Outer", "[BehaviorOrder(Phase.Transaction)]\n[RunsBefore(typeof(Inner))]"),
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX008"]);
    }

    [Test]
    public async Task KRX008_Two_Behaviors_In_A_Cycle(CancellationToken cancellationToken)
    {
        var diagnostics = await Krx008Async(Behavior("A", "[RunsAfter(typeof(B))]") + Behavior("B", "[RunsAfter(typeof(A))]"), cancellationToken);

        await Assert.That(Messages(diagnostics, "KRX008")).IsEquivalentTo(["'A' is part of a RunsAfter/RunsBefore cycle between 'A', 'B', so no order satisfies them"]);
    }

    [Test]
    public async Task KRX008_Three_Behaviors_In_A_Cycle(CancellationToken cancellationToken)
    {
        var diagnostics = await Krx008Async(
            Behavior("A", "[RunsBefore(typeof(B))]") + Behavior("B", "[RunsBefore(typeof(C))]") + Behavior("C", "[RunsBefore(typeof(A))]"),
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX008")).IsEquivalentTo(["'A' is part of a RunsAfter/RunsBefore cycle between 'A', 'B', 'C', so no order satisfies them"]);
    }

    [Test]
    public async Task KRX008_A_Behavior_That_Names_Itself(CancellationToken cancellationToken)
    {
        var diagnostics = await Krx008Async(Behavior("A", "[RunsAfter(typeof(A))]"), cancellationToken);

        await Assert.That(Messages(diagnostics, "KRX008")).IsEquivalentTo(["'A' declares RunsAfter itself, a cycle no order satisfies"]);
    }

    [Test]
    public async Task KRX008_Edges_That_Agree_Or_Name_No_Behavior_Report_Nothing(CancellationToken cancellationToken)
    {
        // Third is a stream behavior: an edge to a class without a behavior of the pipeline's kind is ignored for that kind.
        var diagnostics = await Krx008Async(
            Behavior("Inner", "[BehaviorOrder(Phase.Transaction)]\n[RunsAfter(typeof(Outer))]\n[RunsAfter(typeof(string))]")
          + Behavior("Outer", "[BehaviorOrder(Phase.Observability)]\n[RunsBefore(typeof(Inner))]\n[RunsBefore(typeof(Third))]")
          + """
            [StreamBehavior]
            [BehaviorOrder(Phase.Outermost)]
            public static class Third
            {
                public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                    where TRequest : IStreamRequest<TItem>
                    where TNext : struct, IStreamNext<TItem> =>
                    next.InvokeAsync(cancellationToken);
            }
            """,
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task KRX008_A_Method_BehaviorOrder_Overrides_The_Class_For_Its_Kind(CancellationToken cancellationToken)
    {
        // Late runs after Early in the class's phase for both kinds; its stream method moves to Phase.Outermost, which contradicts the edge for
        // stream pipelines only.
        var diagnostics = await Krx008Async(
            TwoKinds("Late", "[RunsAfter(typeof(Early))]", streamAttributes: "[BehaviorOrder(Phase.Outermost)]") + TwoKinds("Early"),
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX008"))
                    .IsEquivalentTo(
                         [
                             "'Late' declares RunsAfter 'Early' for its stream behavior, but its position (Phase.Outermost, order 0) puts it before (outside) 'Early' (Phase.Default, order 0); RunsAfter and RunsBefore only order behaviors that have the same phase and order",
                         ]
                     );
    }

    [Test]
    public async Task KRX008_A_Method_RunsAfter_Overrides_The_Class_And_Is_Reported_Per_Kind(CancellationToken cancellationToken)
    {
        // Both kinds of Late contradict their phase: the class edge for the request kind, the stream method's own edge for the stream kind.
        var diagnostics = await Krx008Async(
            TwoKinds("Late", "[BehaviorOrder(Phase.Observability)]\n[RunsAfter(typeof(Early))]", streamAttributes: "[RunsAfter(typeof(Early))]")
          + TwoKinds("Early", "[BehaviorOrder(Phase.Transaction)]"),
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX008"))
                    .IsEquivalentTo(
                         [
                             "'Late' declares RunsAfter 'Early', but its position (Phase.Observability, order 0) puts it before (outside) 'Early' (Phase.Transaction, order 0); RunsAfter and RunsBefore only order behaviors that have the same phase and order",
                             "'Late' declares RunsAfter 'Early' for its stream behavior, but its position (Phase.Observability, order 0) puts it before (outside) 'Early' (Phase.Transaction, order 0); RunsAfter and RunsBefore only order behaviors that have the same phase and order",
                         ]
                     );
    }

    [Test]
    public async Task KRX008_A_Method_RunsAfter_List_Replaces_The_Class_List(CancellationToken cancellationToken)
    {
        // The stream method's empty-of-Early list replaces the class's contradicting edge, so only the request kind reports it.
        var diagnostics = await Krx008Async(
            TwoKinds("Late", "[BehaviorOrder(Phase.Observability)]\n[RunsAfter(typeof(Early))]", streamAttributes: "[RunsAfter(typeof(string))]")
          + TwoKinds("Early", "[BehaviorOrder(Phase.Transaction)]"),
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX008"))
                    .IsEquivalentTo(
                         [
                             "'Late' declares RunsAfter 'Early', but its position (Phase.Observability, order 0) puts it before (outside) 'Early' (Phase.Transaction, order 0); RunsAfter and RunsBefore only order behaviors that have the same phase and order",
                         ]
                     );
    }

    [Test]
    public async Task Diagnostics_Point_At_The_Attribute(CancellationToken cancellationToken)
    {
        var diagnostics = await Krx008Async(
            Behavior("Inner", "[BehaviorOrder(Phase.Observability)]\n[RunsAfter(typeof(Outer))]") + Behavior("Outer", "[BehaviorOrder(Phase.Transaction)]"),
            cancellationToken
        );

        var diagnostic = diagnostics.Single();
        await Assert.That(diagnostic.Location.SourceTree!.GetText(cancellationToken).ToString(diagnostic.Location.SourceSpan)).IsEqualTo("RunsAfter(typeof(Outer))");
        await Assert.That(diagnostic.Descriptor.CustomTags).Contains(WellKnownDiagnosticTags.CompilationEnd);
        await Assert.That(string.Create(CultureInfo.InvariantCulture, $"{diagnostic.Severity}")).IsEqualTo("Error");
    }

    private Task<ImmutableArray<Diagnostic>> Krx008Async(string behaviors, CancellationToken cancellationToken) =>
        AnalyzeAsync<BehaviorAnalyzer>(Requests + behaviors, cancellationToken);

    // KRX009 -----------------------------------------------------------------------------------------------------------------------------

    [Test]
    public async Task KRX009_Attached_Behaviors_That_Do_Not_Fit(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(
            Requests
          + Behavior("Open")
          + """
            [RequestBehavior]
            public static class CommandsOnly
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                    where TRequest : IRequest<TResponse>, ICommand
                    where TNext : struct, INext<TResponse> =>
                    next.InvokeAsync(cancellationToken);
            }

            [RequestBehavior]
            public static class ForCount
            {
                public static ValueTask<int> Handle<TNext>(Count request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<int> =>
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

            [StreamBehavior]
            public static class WrapNames
            {
                public static IAsyncEnumerable<string> Handle<TNext>(Names request, TNext next, CancellationToken cancellationToken) where TNext : struct, IStreamNext<string> =>
                    next.InvokeAsync(cancellationToken);
            }

            [NotificationBehavior]
            public static class OnlyPlaced
            {
                public static ValueTask Handle<TNext>(Placed notification, TNext next) where TNext : struct, INext => next.InvokeAsync(default);
            }

            public static partial class PingHandler
            {
                [Behavior(typeof(CommandsOnly))]
                [Behavior(typeof(ForCount))]
                [Behavior(typeof(WrapNames))]
                [Behavior(typeof(string))]
                [Behavior(typeof(Open))]
                private static string Handle(Ping request) => "";
            }

            public static partial class NamesHandler
            {
                [Behavior(typeof(Open))]
                [Behavior(typeof(WrapNames))]
                private static IAsyncEnumerable<string> Handle(Names request) => null!;
            }

            [Behavior(typeof(Open))]
            [Behavior(typeof(OnPublish))]
            [Behavior(typeof(OnlyPlaced))]
            public sealed record Shipped : INotification;
            """,
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX009"))
                    .IsEquivalentTo(
                         [
                             "Behavior 'CommandsOnly' does not fit 'PingHandler.Handle(Ping)': 'Ping' does not satisfy the constraint 'TRequest : ICommand'",
                             "Behavior 'ForCount' does not fit 'PingHandler.Handle(Ping)': its request behavior method is closed over 'Count' and 'int', but the target needs exactly 'Ping' and 'string'",
                             "Behavior 'Open' does not fit 'NamesHandler.Handle(Names)': it has no stream behavior method (the class does not carry [StreamBehavior])",
                             "Behavior 'Open' does not fit 'Shipped': it has no notification behavior method (the class does not carry [NotificationBehavior])",
                             "Behavior 'OnlyPlaced' does not fit 'Shipped': its notification behavior method is closed over 'Placed', but the target needs exactly 'Shipped'",
                             "Behavior 'WrapNames' does not fit 'PingHandler.Handle(Ping)': it has no request behavior method (the class does not carry [RequestBehavior])",
                             "Behavior 'string' does not fit 'PingHandler.Handle(Ping)': it is not a behavior class (a static class with [RequestBehavior], [StreamBehavior] or [NotificationBehavior])",
                         ]
                     );
        await Assert.That(Ids(diagnostics).Distinct()).IsEquivalentTo(["KRX009"]);
    }

    [Test]
    public async Task KRX009_Stream_Behaviors_Whose_Constraints_Do_Not_Hold(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(
            Requests
          + """
            [StreamBehavior]
            public static class ValueItems
            {
                public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                    where TRequest : IStreamRequest<TItem>
                    where TItem : struct
                    where TNext : struct, IStreamNext<TItem> =>
                    next.InvokeAsync(cancellationToken);
            }

            [StreamBehavior]
            public static class CommandItems
            {
                public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                    where TRequest : IStreamRequest<TItem>, ICommand
                    where TNext : struct, IStreamNext<TItem> =>
                    next.InvokeAsync(cancellationToken);
            }

            public sealed record Ticks : IStreamRequest<int>, ICommand;

            public static partial class NamesHandler
            {
                [Behavior(typeof(ValueItems))]
                [Behavior(typeof(CommandItems))]
                private static IAsyncEnumerable<string> Handle(Names request) => null!;
            }

            public static partial class TicksHandler
            {
                [Behavior(typeof(ValueItems))]
                [Behavior(typeof(CommandItems))]
                private static IAsyncEnumerable<int> Handle(Ticks request) => null!;
            }
            """,
            cancellationToken
        );

        // Checked on both type arguments: TRequest and TItem.
        await Assert.That(Messages(diagnostics, "KRX009"))
                    .IsEquivalentTo(
                         [
                             "Behavior 'CommandItems' does not fit 'NamesHandler.Handle(Names)': 'Names' does not satisfy the constraint 'TRequest : ICommand'",
                             "Behavior 'ValueItems' does not fit 'NamesHandler.Handle(Names)': 'string' does not satisfy the constraint 'TItem : struct'",
                         ]
                     );
        await Assert.That(Ids(diagnostics).Distinct()).IsEquivalentTo(["KRX009"]);
    }

    [Test]
    public async Task KRX009_Constraint_Types_Nested_In_Generic_Types(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(
            Requests
          + """
            public sealed class Outer<T> { public interface IMarker { } public interface IInner<U> { } }
            public sealed record Marked : IRequest<string>, Outer<string>.IMarker, Outer<string>.IInner<Marked>;

            [RequestBehavior]
            public static class Nested
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                    where TRequest : IRequest<TResponse>, Outer<TResponse>.IMarker
                    where TNext : struct, INext<TResponse> =>
                    next.InvokeAsync(cancellationToken);
            }

            [RequestBehavior]
            public static class NestedGeneric
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                    where TRequest : IRequest<TResponse>, Outer<TResponse>.IInner<TRequest>
                    where TNext : struct, INext<TResponse> =>
                    next.InvokeAsync(cancellationToken);
            }

            public static partial class PingHandler
            {
                [Behavior(typeof(Nested))]
                [Behavior(typeof(NestedGeneric))]
                private static string Handle(Ping request) => "";
            }

            public static partial class MarkedHandler
            {
                [Behavior(typeof(Nested))]
                [Behavior(typeof(NestedGeneric))]
                private static string Handle(Marked request) => "";
            }
            """,
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX009"))
                    .IsEquivalentTo(
                         [
                             "Behavior 'Nested' does not fit 'PingHandler.Handle(Ping)': 'Ping' does not satisfy the constraint 'TRequest : Outer<string>.IMarker'",
                             "Behavior 'NestedGeneric' does not fit 'PingHandler.Handle(Ping)': 'Ping' does not satisfy the constraint 'TRequest : Outer<string>.IInner<Ping>'",
                         ]
                     );
        await Assert.That(Ids(diagnostics).Distinct()).IsEquivalentTo(["KRX009"]);
    }

    [Test]
    public async Task KRX009_Annotated_Response_Type_Parameters_Fit_With_Nullability(CancellationToken cancellationToken)
    {
        // OQ-58: 'TResponse?' ('TItem?') fits an annotated, oblivious or value type response, and not a non-annotated reference one; on a
        // type parameter constrained to struct it is Nullable<T>, which fits only a nullable value type response.
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(
            Requests
          + """
            public sealed record Find : IRequest<string?>;
            public sealed record FindAll : IStreamRequest<string?>;
            public sealed record Maybe : IRequest<int?>;
            #nullable disable
            public sealed record Legacy : IRequest<string>;
            #nullable enable

            [RequestBehavior]
            [StreamBehavior]
            public static class Nullable
            {
                public static ValueTask<TResponse?> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                    where TRequest : IRequest<TResponse?>
                    where TNext : struct, INext<TResponse?> =>
                    next.InvokeAsync(cancellationToken);

                public static IAsyncEnumerable<TItem?> HandleAsync<TRequest, TItem, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                    where TRequest : IStreamRequest<TItem?>
                    where TNext : struct, IStreamNext<TItem?> =>
                    next.InvokeAsync(cancellationToken);
            }

            [RequestBehavior]
            public static class NullableNext
            {
                public static ValueTask<TResponse?> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                    where TRequest : IRequest<TResponse>
                    where TNext : struct, INext<TResponse?> =>
                    next.InvokeAsync(cancellationToken);
            }

            [RequestBehavior]
            public static class NullableValue
            {
                public static ValueTask<TResponse?> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                    where TRequest : IRequest<TResponse?>
                    where TResponse : struct
                    where TNext : struct, INext<TResponse?> =>
                    next.InvokeAsync(cancellationToken);
            }

            public static partial class PingHandler
            {
                [Behavior(typeof(Nullable))]
                [Behavior(typeof(NullableNext))]
                private static string Handle(Ping request) => "";
            }

            public static partial class FindHandler
            {
                [Behavior(typeof(Nullable))]
                [Behavior(typeof(NullableNext))]
                private static string? Handle(Find request) => null;
            }

            public static partial class LegacyHandler
            {
                [Behavior(typeof(Nullable))]
                [Behavior(typeof(NullableNext))]
                private static string Handle(Legacy request) => "";
            }

            public static partial class CountHandler
            {
                [Behavior(typeof(Nullable))]
                [Behavior(typeof(NullableValue))]
                private static int Handle(Count request) => 1;
            }

            public static partial class MaybeHandler
            {
                [Behavior(typeof(Nullable))]
                [Behavior(typeof(NullableValue))]
                private static int? Handle(Maybe request) => null;
            }

            public static partial class NamesHandler
            {
                [Behavior(typeof(Nullable))]
                private static IAsyncEnumerable<string> Handle(Names request) => null!;
            }

            public static partial class FindAllHandler
            {
                [Behavior(typeof(Nullable))]
                private static IAsyncEnumerable<string?> Handle(FindAll request) => null!;
            }
            """,
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX009"))
                    .IsEquivalentTo(
                         [
                             "Behavior 'Nullable' does not fit 'NamesHandler.Handle(Names)': 'Names' does not satisfy the constraint 'TRequest : IStreamRequest<string?>'",
                             "Behavior 'Nullable' does not fit 'PingHandler.Handle(Ping)': 'Ping' does not satisfy the constraint 'TRequest : IRequest<string?>'",
                             "Behavior 'NullableNext' does not fit 'PingHandler.Handle(Ping)': its request behavior method's 'next' carries 'TResponse?', which is 'string?' for this target, but the target's response type is 'string'",
                             "Behavior 'NullableValue' does not fit 'CountHandler.Handle(Count)': its request behavior method's 'next' carries 'TResponse?', which is never the response type 'int'",
                         ]
                     );
        await Assert.That(Ids(diagnostics).Distinct()).IsEquivalentTo(["KRX009"]);
    }

    [Test]
    public async Task Assembly_Behavior_Naming_A_Non_Behavior_Reports_Nothing(CancellationToken cancellationToken)
    {
        // An [assembly: Behavior] that is not a behavior class matches no target (ADR 0001, Matching).
        const string source = "[assembly: Behavior(typeof(string))]\n[assembly: Behavior(typeof(Placed))]\n" + Requests;

        await Assert.That(Ids(await AnalyzeAsync<BehaviorAnalyzer>(source, cancellationToken))).IsEmpty();
        await Assert.That(Ids(await AnalyzeAsync<MessageTypeAnalyzer>(source, cancellationToken, projectName: "Messages"))).IsEmpty();
    }

    // KRX019 -----------------------------------------------------------------------------------------------------------------------------

    [Test]
    [Arguments(
        "[RequestBehavior] public sealed class X { public static ValueTask<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<string> => n.InvokeAsync(c); }",
        "is not static; a behavior is a static class whose behavior methods the pipeline calls directly"
    )]
    [Arguments(
        "[RequestBehavior] public static class X<T> { public static ValueTask<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<string> => n.InvokeAsync(c); }",
        "is generic; a behavior method carries the type parameters, its class does not"
    )]
    [Arguments(
        "[RequestBehavior, StreamBehavior] public static class X { public static ValueTask<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<string> => n.InvokeAsync(c); }",
        "carries [StreamBehavior] but declares no stream behavior method; add a static Handle or HandleAsync whose 'next' is a type parameter constrained 'struct, IStreamNext<TItem>'"
    )]
    [Arguments(
        "[RequestBehavior] public static class X { public static ValueTask<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<string> => n.InvokeAsync(c); public static ValueTask<int> HandleAsync<TNext>(Count r, TNext n, CancellationToken c) where TNext : struct, INext<int> => n.InvokeAsync(c); }",
        "declares more than one request behavior method; a behavior class declares exactly one behavior method of each kind it carries"
    )]
    [Arguments(
        "[RequestBehavior] public static class X { public static ValueTask<string> Handle<TRequest, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<string> where TNext : struct, INext<string> => n.InvokeAsync(c); }",
        "is partially open: its message is the type parameter 'TRequest', but its 'next' carries 'string'; an open behavior method's 'next' carries the response (or item) type parameter, as in INext<TResponse>"
    )]
    [Arguments(
        "[RequestBehavior] public static class X { public static ValueTask<TResponse> Handle<TResponse, TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }",
        "is partially open: its message 'Ping' is closed, but its 'next' carries 'TResponse'; a closed behavior method is generic only in 'next'"
    )]
    [Arguments(
        "[RequestBehavior] public static class X { public static ValueTask<TResponse> Handle<TRequest, TResponse, T, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<TResponse> where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }",
        "has type parameters other than its message, response and 'next' ones ('T'); an open behavior method is generic only in those"
    )]
    [Arguments(
        "[RequestBehavior] public static class X { public static ValueTask<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : INext<string> => n.InvokeAsync(c); }",
        "has a 'next' type parameter 'TNext' without the 'struct' constraint; 'next' is the generated link struct, constrained 'struct, INext<TResponse>'"
    )]
    [Arguments(
        "[RequestBehavior] public static class X { public static Task<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<string> => n.InvokeAsync(c).AsTask(); }",
        "returns 'Task<string>'; a request behavior method returns ValueTask<string>"
    )]
    [Arguments(
        "[StreamBehavior] public static class X { public static ValueTask<string> Handle<TNext>(Names r, TNext n, CancellationToken c) where TNext : struct, IStreamNext<string> => default; }",
        "returns 'ValueTask<string>'; a stream behavior method returns IAsyncEnumerable<string>"
    )]
    [Arguments(
        "[RequestBehavior] public static class X { public static ValueTask<int> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<int> => n.InvokeAsync(c); }",
        "is closed over 'Ping', but its 'next' carries 'int' instead of 'string'; a closed behavior method's 'next' carries exactly the request's response type"
    )]
    [Arguments(
        "[RequestBehavior] public static class X { public static ValueTask<string> Handle<TNext>(Names r, TNext n, CancellationToken c) where TNext : struct, INext<string> => n.InvokeAsync(c); }",
        "has a 'next' constraint and a message type of different kinds: 'Names' is a stream message, but its 'next' is INext<TResponse>"
    )]
    [Arguments(
        "[RequestBehavior] public static class X { public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IStreamRequest<TResponse> where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }",
        "has a 'next' constraint and a message type of different kinds: 'TRequest' is constrained to a stream message, but its 'next' is INext<TResponse>"
    )]
    [Arguments(
        "[RequestBehavior] public static class X { public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }",
        "has an open message type parameter 'TRequest' without the request message constraint; add 'where TRequest : IRequest<TResponse>'"
    )]
    [Arguments(
        "[StreamBehavior] public static class X { public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest r, TNext n, CancellationToken c) where TNext : struct, IStreamNext<TItem> => n.InvokeAsync(c); }",
        "has an open message type parameter 'TRequest' without the stream message constraint; add 'where TRequest : IStreamRequest<TItem>'"
    )]
    [Arguments(
        "[NotificationBehavior] public static class X { public static ValueTask Handle<TNotification, TNext>(TNotification m, TNext n, CancellationToken c) where TNext : struct, INext => n.InvokeAsync(c); }",
        "has an open message type parameter 'TNotification' without the notification message constraint; add 'where TNotification : INotification'"
    )]
    // A constraint to an interface that does not derive from the kind's message interface does not count (OQ-57).
    [Arguments(
        "[RequestBehavior] public static class X { public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : ICommand where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }",
        "has an open message type parameter 'TRequest' without the request message constraint; add 'where TRequest : IRequest<TResponse>'"
    )]
    [Arguments(
        "[StreamBehavior] public static class X { public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : ICommand where TNext : struct, IStreamNext<TItem> => n.InvokeAsync(c); }",
        "has an open message type parameter 'TRequest' without the stream message constraint; add 'where TRequest : IStreamRequest<TItem>'"
    )]
    [Arguments(
        "[NotificationBehavior] public static class X { public static ValueTask Handle<TNotification, TNext>(TNotification m, TNext n, CancellationToken c) where TNotification : ICommand where TNext : struct, INext => n.InvokeAsync(c); }",
        "has an open message type parameter 'TNotification' without the notification message constraint; add 'where TNotification : INotification'"
    )]
    // Through a derived interface, the response or item type parameter must be the one the interface passes on.
    [Arguments(
        "public interface ICommand<T> : IRequest<T> { } [RequestBehavior] public static class X { public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : ICommand<int> where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }",
        "has an open message type parameter 'TRequest' without the request message constraint; add 'where TRequest : IRequest<TResponse>'"
    )]
    [Arguments(
        "[RequestBehavior] public static class X { public static ValueTask<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<string> => n.InvokeAsync(c); public static IAsyncEnumerable<string> HandleAsync<TNext>(Names r, TNext n, CancellationToken c) where TNext : struct, IStreamNext<string> => n.InvokeAsync(c); }",
        "is a stream behavior method (its 'next' is IStreamNext<TItem>), but the class does not carry [StreamBehavior]"
    )]
    [Arguments(
        "[RequestBehavior] public static class X { public static ValueTask<string> Handle(Ping r) => default; }",
        "has no 'next' parameter; a behavior method takes 'next' as a method type parameter constrained 'struct' and INext<TResponse> (request), IStreamNext<TItem> (stream) or INext (notification)"
    )]
    [Arguments(
        "[RequestBehavior] public static class X { public static ValueTask<string> Handle(Ping r, INext<string> n, CancellationToken c) => n.InvokeAsync(c); }",
        "takes 'next' as 'INext<string>'; 'next' is a method type parameter constrained 'struct, INext<TResponse>'"
    )]
    public async Task KRX019_Unsupported_Behavior_Shapes(string behavior, string reason, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(Requests + behavior, cancellationToken);

        await Assert.That(Ids(diagnostics).Distinct()).IsEquivalentTo(["KRX019"]);
        await Assert.That(Messages(diagnostics, "KRX019")).Contains(m => m.EndsWith(reason, StringComparison.Ordinal));
    }

    [Test]
    public async Task KRX019_Valid_Behaviors_Report_Nothing(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(
            Requests
          + """
            // One class with all three kinds: Handle and HandleAsync side by side.
            [RequestBehavior]
            [StreamBehavior]
            [NotificationBehavior]
            [GlobalBehavior]
            [BehaviorOrder(Phase.Observability, Order = 2)]
            public static class Everywhere
            {
                public static async ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(
                    TRequest request,
                    TNext next,
                    IAudit audit,
                    [FromKeyedServices("k")] IAudit? keyed = null,
                    CancellationToken cancellationToken = default
                )
                    where TRequest : IRequest<TResponse>
                    where TNext : struct, INext<TResponse> =>
                    await next.InvokeAsync(cancellationToken);

                public static IAsyncEnumerable<TItem> HandleAsync<TRequest, TItem, TNext>(TNext next, TRequest request, CancellationToken cancellationToken)
                    where TRequest : IStreamRequest<TItem>
                    where TNext : struct, IStreamNext<TItem> =>
                    next.InvokeAsync(cancellationToken);

                [BehaviorOrder(Phase.Outermost)]
                public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, IServiceProvider services, CancellationToken cancellationToken)
                    where TNotification : INotification
                    where TNext : struct, INext =>
                    next.InvokeAsync(cancellationToken);
            }

            // Open methods with the message constraint plus further constraints, of each kind.
            [RequestBehavior]
            public static class Commands
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next)
                    where TRequest : class, IRequest<TResponse>, ICommand
                    where TResponse : notnull
                    where TNext : struct, INext<TResponse> =>
                    next.InvokeAsync(default);
            }

            [StreamBehavior]
            public static class ValueItems
            {
                public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest request, TNext next)
                    where TRequest : IStreamRequest<TItem>, ICommand
                    where TItem : struct
                    where TNext : struct, IStreamNext<TItem> =>
                    next.InvokeAsync(default);
            }

            public interface IDomainEvent : INotification { }

            [NotificationBehavior]
            public static class DomainEvents
            {
                public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next) where TNotification : IDomainEvent where TNext : struct, INext =>
                    next.InvokeAsync(default);
            }

            // Closed methods need no message constraint; an IRequest's next carries Unit.
            public sealed record Delete : IRequest;

            [RequestBehavior]
            public static class ClosedDelete
            {
                internal static ValueTask<Unit> HandleAsync<TNext>(Delete request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<Unit> =>
                    next.InvokeAsync(cancellationToken);
            }

            [NotificationBehavior]
            public static class ClosedPlaced
            {
                public static ValueTask Handle<TNext>(TNext next, Placed notification) where TNext : struct, INext => next.InvokeAsync(default);
            }

            public static partial class PingHandler
            {
                [Behavior(typeof(Everywhere))]
                private static string Handle(Ping request) => "";
            }

            public static partial class DeleteHandler
            {
                [Behavior(typeof(ClosedDelete))]
                private static void Handle(Delete request) { }
            }

            [Behavior(typeof(ClosedPlaced))]
            [Behavior(typeof(Everywhere))]
            public sealed record Moved : INotification;
            """,
            cancellationToken
        );

        // Moved is not Placed: the closed notification behavior does not fit it (KRX009); nothing else is reported.
        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX009"]);
    }

    // KRX013, KRX018 ----------------------------------------------------------------------------------------------------------------------

    [Test]
    [Arguments("ref IAudit audit", "Parameter 'audit' of behavior method 'X.Handle' is 'ref'; behavior method parameters are passed by value")]
    [Arguments("params IAudit[] audits", "Parameter 'audits' of behavior method 'X.Handle' is 'params'; behavior method parameters are passed one by one")]
    [Arguments("Span<int> buffer", "Parameter 'buffer' of behavior method 'X.Handle' is a ref struct, which the pipeline context cannot hold")]
    [Arguments("CancellationToken second", "Parameter 'second' of behavior method 'X.Handle' is a second CancellationToken; a behavior method takes at most one")]
    public async Task KRX013_Behavior_Method_Parameters(string parameter, string message, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(
            Requests
          + $$"""
              [RequestBehavior]
              public static class X
              {
                  public static ValueTask<string> Handle<TNext>(Ping request, TNext next, CancellationToken cancellationToken, {{parameter}}) where TNext : struct, INext<string> =>
                      next.InvokeAsync(cancellationToken);
              }
              """,
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX013")).IsEquivalentTo([message]);
        await Assert.That(Ids(diagnostics).Distinct()).IsEquivalentTo(["KRX013"]);
    }

    [Test]
    [Arguments("[FromKeyedServices] IAudit audit", "Parameter 'audit' uses [FromKeyedServices] without a key; give the key as a compile-time constant")]
    public async Task KRX018_Behavior_Method_Keys(string parameter, string message, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(
            Requests
          + $$"""
              [RequestBehavior]
              public static class X
              {
                  public static ValueTask<string> Handle<TNext>(Ping request, TNext next, {{parameter}}) where TNext : struct, INext<string> =>
                      next.InvokeAsync(default);
              }
              """,
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX018")).IsEquivalentTo([message]);
        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX018"]);
    }

    [Test]
    public async Task KRX013_And_KRX018_Valid_Behavior_Method_Parameters_Report_Nothing(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(
            Requests
          + """
            [RequestBehavior]
            public static class X
            {
                public const string Key = "audit";

                public static ValueTask<string> Handle<TNext>(
                    IServiceProvider services,
                    [FromKeyedServices(Key)] IAudit keyed,
                    [FromKeyedServices(42)] IAudit? numbered,
                    Ping request,
                    int? retries,
                    TNext next,
                    CancellationToken cancellationToken
                )
                    where TNext : struct, INext<string> =>
                    next.InvokeAsync(cancellationToken);
            }
            """,
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task KRX020_Is_Retired(CancellationToken cancellationToken)
    {
        // Behaviors are static classes and have no constructors: no analyzer reports KRX020 any more.
        var ids = typeof(Descriptors).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                                     .Select(static f => f.GetValue(null))
                                     .OfType<DiagnosticDescriptor>()
                                     .Select(static d => d.Id)
                                     .Concat(new BehaviorAnalyzer().SupportedDiagnostics.Select(static d => d.Id))
                                     .ToArray();
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(
            Requests + "[RequestBehavior] public static class X { static X() { } public static ValueTask<string> Handle<TNext>(Ping r, TNext n) where TNext : struct, INext<string> => n.InvokeAsync(default); }",
            cancellationToken
        );

        await Assert.That(ids).DoesNotContain("KRX020");
        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    // KRX042 -----------------------------------------------------------------------------------------------------------------------------

    [Test]
    public async Task KRX042_Behavior_Attributes_Without_Effect(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(
            Requests
          + Behavior("Open")
          + """
            [Behavior(typeof(Open))]
            public sealed class NotANotification { [Behavior(typeof(Open))] public void Handle(int value) { } }

            [GlobalBehavior]
            public static class NotABehavior { }

            [RequestBehavior]
            public static class WithHelpers
            {
                public static ValueTask<string> Handle<TNext>(Ping request, TNext next) where TNext : struct, INext<string> => next.InvokeAsync(default);

                [GlobalBehavior]
                public static string Describe() => "";
            }

            public static class NotAClass
            {
                [GlobalBehavior]
                public static ValueTask<string> Handle<TNext>(Ping request, TNext next) where TNext : struct, INext<string> => next.InvokeAsync(default);
            }

            public interface IDomainEvent : INotification { }

            public static partial class PlacedHandler
            {
                [Behavior(typeof(Open))]
                private static ValueTask Handle(Placed notification) => default;
            }

            public static partial class AnyEventHandler
            {
                [Behavior(typeof(Open))]
                private static ValueTask Handle(IDomainEvent notification) => default;
            }
            """,
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX042"))
                    .IsEquivalentTo(
                         [
                             "[Behavior] on 'AnyEventHandler.Handle(IDomainEvent)' has no effect, because it is a notification handler; notification behaviors wrap the whole publish, so attach them to the notification type",
                             "[Behavior] on 'NotANotification' has no effect, because it is not a notification type; attach request and stream behaviors to the handler method",
                             "[Behavior] on 'NotANotification.Handle(int)' has no effect, because it is not a handler",
                             "[Behavior] on 'PlacedHandler.Handle(Placed)' has no effect, because it is a notification handler; notification behaviors wrap the whole publish, so attach them to the notification type",
                             "[GlobalBehavior] on 'NotABehavior' has no effect, because it carries no behavior kind attribute ([RequestBehavior], [StreamBehavior] or [NotificationBehavior])",
                             "[GlobalBehavior] on 'NotAClass.Handle<TNext>(Ping, TNext)' has no effect, because it is not a behavior method (a static Handle or HandleAsync of a kind its class carries)",
                             "[GlobalBehavior] on 'WithHelpers.Describe()' has no effect, because it is not a behavior method (a static Handle or HandleAsync of a kind its class carries)",
                         ]
                     );
        await Assert.That(Ids(diagnostics).Distinct()).IsEquivalentTo(["KRX042"]);
    }

    [Test]
    public async Task KRX042_Global_Behavior_On_A_Class_Or_A_Behavior_Method_Reports_Nothing(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(
            Requests
          + Behavior("OnClass", "[GlobalBehavior]")
          + Behavior("OnMethod", methodAttributes: "[GlobalBehavior]")
          + TwoKinds("OnBoth", "[GlobalBehavior]", streamAttributes: "[GlobalBehavior]"),
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    // KRX016 -----------------------------------------------------------------------------------------------------------------------------

    [Test]
    public async Task KRX016_A_Host_Behavior_Narrower_Than_Internal(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(
            "[assembly: KeryxHost]\n"
          + "[assembly: Behavior(typeof(FileLocalAssembly))]\n"
          + "[assembly: Behavior(typeof(PrivateAssemblyMethod))]\n"
          + "[assembly: Behavior(typeof(Outer.Reachable))]\n"
          + Requests
          + """
            public static partial class PingHandler
            {
                [Behavior(typeof(Hidden))]
                [Behavior(typeof(PrivateMethod))]
                [Behavior(typeof(FileLocal))]
                private static string Handle(Ping request) => "";

                [RequestBehavior]
                private static class Hidden
                {
                    public static ValueTask<string> Handle<TNext>(Ping r, TNext n) where TNext : struct, INext<string> => n.InvokeAsync(default);
                }
            }

            [RequestBehavior]
            internal static class PrivateMethod
            {
                private static ValueTask<string> Handle<TNext>(Ping r, TNext n) where TNext : struct, INext<string> => n.InvokeAsync(default);
            }

            internal static class Outer
            {
                [RequestBehavior]
                [GlobalBehavior]
                private static class HiddenGlobal
                {
                    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n) where TRequest : IRequest<TResponse> where TNext : struct, INext<TResponse> => n.InvokeAsync(default);
                }

                [RequestBehavior]
                internal static class HiddenGlobalMethod
                {
                    [GlobalBehavior]
                    private static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n) where TRequest : IRequest<TResponse> where TNext : struct, INext<TResponse> => n.InvokeAsync(default);
                }

                [RequestBehavior]
                [GlobalBehavior]
                internal static class Reachable
                {
                    internal static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n) where TRequest : IRequest<TResponse> where TNext : struct, INext<TResponse> => n.InvokeAsync(default);
                }
            }

            // File-local types count as narrower than internal: generated code in another file cannot name them.
            [RequestBehavior]
            file static class FileLocal
            {
                public static ValueTask<string> Handle<TNext>(Ping r, TNext n) where TNext : struct, INext<string> => n.InvokeAsync(default);
            }

            [RequestBehavior]
            [GlobalBehavior]
            file static class FileLocalGlobal
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n) where TRequest : IRequest<TResponse> where TNext : struct, INext<TResponse> => n.InvokeAsync(default);
            }

            [RequestBehavior]
            file static class FileLocalAssembly
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n) where TRequest : IRequest<TResponse> where TNext : struct, INext<TResponse> => n.InvokeAsync(default);
            }

            [RequestBehavior]
            internal static class PrivateAssemblyMethod
            {
                private static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n) where TRequest : IRequest<TResponse> where TNext : struct, INext<TResponse> => n.InvokeAsync(default);
            }
            """,
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX016"))
                    .IsEquivalentTo(
                         [
                             "'FileLocal' must be at least internal, because 'PingHandler.Handle(Ping)' attaches it and the host's generated mediator calls it from outside its containing type",
                             "'FileLocalAssembly' must be at least internal, because this assembly attaches it to its handlers and the host's generated mediator calls it from outside its containing type",
                             "'FileLocalGlobal' must be at least internal, because it is a global behavior and the host's generated mediator calls it from outside its containing type",
                             "'Outer.HiddenGlobal' must be at least internal, because it is a global behavior and the host's generated mediator calls it from outside its containing type",
                             "'Outer.HiddenGlobalMethod.Handle' must be at least internal, because it is a global behavior and the host's generated mediator calls it from outside its containing type",
                             "'PingHandler.Hidden' must be at least internal, because 'PingHandler.Handle(Ping)' attaches it and the host's generated mediator calls it from outside its containing type",
                             "'PrivateAssemblyMethod.Handle' must be at least internal, because this assembly attaches it to its handlers and the host's generated mediator calls it from outside its containing type",
                             "'PrivateMethod.Handle' must be at least internal, because 'PingHandler.Handle(Ping)' attaches it and the host's generated mediator calls it from outside its containing type",
                         ]
                     );
    }

    [Test]
    public async Task KRX016_Behaviors_Outside_A_Host_Are_Public(CancellationToken cancellationToken)
    {
        const string source = Requests + """
            [RequestBehavior]
            internal static class InternalClass
            {
                public static ValueTask<string> Handle<TNext>(Ping r, TNext n) where TNext : struct, INext<string> => n.InvokeAsync(default);
            }

            [RequestBehavior]
            public static class InternalMethod
            {
                internal static ValueTask<string> Handle<TNext>(Ping r, TNext n) where TNext : struct, INext<string> => n.InvokeAsync(default);
            }

            [NotificationBehavior]
            public static class InternalNotificationMethod
            {
                internal static ValueTask Handle<TNotification, TNext>(TNotification m, TNext n) where TNotification : INotification where TNext : struct, INext => n.InvokeAsync(default);
            }

            [RequestBehavior]
            public static class InternalGlobal
            {
                [GlobalBehavior]
                internal static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n) where TRequest : IRequest<TResponse> where TNext : struct, INext<TResponse> => n.InvokeAsync(default);
            }

            [RequestBehavior]
            [GlobalBehavior]
            public static class PublicGlobal
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n) where TRequest : IRequest<TResponse> where TNext : struct, INext<TResponse> => n.InvokeAsync(default);
            }

            public static partial class PingHandler
            {
                [Behavior(typeof(InternalClass))]
                [Behavior(typeof(InternalMethod))]
                [Behavior(typeof(PublicGlobal))]
                private static string Handle(Ping request) => "";
            }

            [Behavior(typeof(InternalNotificationMethod))]
            public sealed record Shipped : INotification;
            """;

        var handlers = await AnalyzeAsync<HandlerAnalyzer>(source, cancellationToken);
        var messages = await AnalyzeAsync<MessageTypeAnalyzer>(source, cancellationToken, projectName: "Messages");

        await Assert.That(Messages(handlers, "KRX016"))
                    .IsEquivalentTo(
                         [
                             "'InternalClass' must be public, because the handler 'PingHandler.Handle(Ping)' attaches it and this assembly is not a host",
                             "'InternalMethod.Handle' must be public, because the handler 'PingHandler.Handle(Ping)' attaches it and this assembly is not a host",
                         ]
                     );
        await Assert.That(Messages(messages, "KRX016"))
                    .IsEquivalentTo(
                         [
                             "'InternalGlobal.Handle' must be public, because it is a global behavior, every host that references this assembly applies it, and this assembly is not a host",
                             "'InternalNotificationMethod.Handle' must be public, because the notification 'Shipped' attaches it and this assembly is not a host",
                         ]
                     );
    }
}
