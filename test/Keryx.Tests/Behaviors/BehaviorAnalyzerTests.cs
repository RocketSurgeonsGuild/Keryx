using System.Collections.Immutable;
using System.Globalization;
using Keryx.Analyzers.Diagnostics;
using Keryx.Tests.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Keryx.Tests.Behaviors;

/// <summary>
///     KRX008, KRX009, KRX016 (behaviors in a host), KRX018 (behavior constructors), KRX019, KRX020 and KRX042, each with positive and
///     negative cases. Every KRX008 case runs with the <c>typeof</c> form, the generic form and both mixed, and must report the same.
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

        """;

    /// <summary>A closed request behavior over <c>(Ping, string)</c> named <paramref name="name" />, after its attributes.</summary>
    private static string Behavior(string name, string attributes = "") =>
        $$"""
          {{attributes}}
          public sealed class {{name}} : IRequestPipelineBehavior<Ping, string>
          {
              public ValueTask<string> Handle<TNext>(Ping request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<string> =>
                  next.InvokeAsync(cancellationToken);
          }

          """;

    private const string Open = """
        public sealed class Open<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
        {
            public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse> =>
                next.InvokeAsync(cancellationToken);
        }

        """;

    // KRX008 -----------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     An inert edge (to a type that is not a behavior) placed before the edge a KRX008 case is about, on the same class: under
    ///     <see cref="OrderingForm.Mixed" /> the filler uses the <c>typeof</c> form and the significant edge the generic form, so every case also
    ///     runs with both forms on one class.
    /// </summary>
    private const string Filler = "{{After:string}}\n";

    [Test]
    [MethodDataSource(nameof(Forms))]
    public async Task KRX008_RunsAfter_Contradicts_Phase(OrderingForm form, CancellationToken cancellationToken)
    {
        var diagnostics = await Krx008Async(
            form,
            Behavior("Inner", "[BehaviorOrder(Phase.Observability)]\n" + Filler + "{{After:Outer}}") + Behavior("Outer", "[BehaviorOrder(Phase.Transaction)]"),
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
    [MethodDataSource(nameof(Forms))]
    public async Task KRX008_RunsBefore_Contradicts_Phase(OrderingForm form, CancellationToken cancellationToken)
    {
        var diagnostics = await Krx008Async(
            form,
            Behavior("Outer", "[BehaviorOrder(Phase.Transaction)]\n" + Filler + "{{Before:Inner}}") + Behavior("Inner", "[BehaviorOrder(Phase.Observability)]"),
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
    [MethodDataSource(nameof(Forms))]
    public async Task KRX008_Edge_Contradicts_Order(OrderingForm form, CancellationToken cancellationToken)
    {
        var diagnostics = await Krx008Async(
            form,
            Behavior("First", "[BehaviorOrder(Phase.Default, Order = 1)]\n" + Filler + "{{Before:Second}}") + Behavior("Second", "[BehaviorOrder(Phase.Default, Order = 0)]"),
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
    [MethodDataSource(nameof(Forms))]
    public async Task KRX008_Is_Checked_Once_Per_Pair(OrderingForm form, CancellationToken cancellationToken)
    {
        // The same contradiction declared from both sides: Inner runs after Outer, and Outer runs before Inner.
        var diagnostics = await Krx008Async(
            form,
            Behavior("Inner", "[BehaviorOrder(Phase.Observability)]\n" + Filler + "{{After:Outer}}")
          + Behavior("Outer", "[BehaviorOrder(Phase.Transaction)]\n" + Filler + "{{Before:Inner}}"),
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX008"]);
    }

    [Test]
    [MethodDataSource(nameof(Forms))]
    public async Task KRX008_Two_Behaviors_In_A_Cycle(OrderingForm form, CancellationToken cancellationToken)
    {
        var diagnostics = await Krx008Async(form, Behavior("A", Filler + "{{After:B}}") + Behavior("B", Filler + "{{After:A}}"), cancellationToken);

        await Assert.That(Messages(diagnostics, "KRX008")).IsEquivalentTo(["'A' is part of a RunsAfter/RunsBefore cycle between 'A', 'B', so no order satisfies them"]);
    }

    [Test]
    [MethodDataSource(nameof(Forms))]
    public async Task KRX008_Three_Behaviors_In_A_Cycle(OrderingForm form, CancellationToken cancellationToken)
    {
        var diagnostics = await Krx008Async(
            form,
            Behavior("A", Filler + "{{Before:B}}") + Behavior("B", Filler + "{{Before:C}}") + Behavior("C", Filler + "{{Before:A}}"),
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX008")).IsEquivalentTo(["'A' is part of a RunsAfter/RunsBefore cycle between 'A', 'B', 'C', so no order satisfies them"]);
    }

    [Test]
    [MethodDataSource(nameof(Forms))]
    public async Task KRX008_A_Behavior_That_Names_Itself(OrderingForm form, CancellationToken cancellationToken)
    {
        var diagnostics = await Krx008Async(form, Behavior("A", Filler + "{{After:A}}"), cancellationToken);

        await Assert.That(Messages(diagnostics, "KRX008")).IsEquivalentTo(["'A' declares RunsAfter itself, a cycle no order satisfies"]);
    }

    [Test]
    [MethodDataSource(nameof(Forms))]
    public async Task KRX008_An_Edge_To_A_Closing_Of_A_Generic_Behavior_Contradicts_Phase(OrderingForm form, CancellationToken cancellationToken)
    {
        // Open<,> is in Phase.Default; an edge to one closing counts as an edge to the class (open-questions OQ-37).
        var diagnostics = await Krx008Async(form, Behavior("Early", "[BehaviorOrder(Phase.Observability)]\n" + Filler + "{{After:Open<Ping, string>}}") + Open, cancellationToken);

        await Assert.That(Messages(diagnostics, "KRX008"))
                    .IsEquivalentTo(
                         [
                             "'Early' declares RunsAfter 'Open<Ping, string>', but its position (Phase.Observability, order 0) puts it before (outside) 'Open<Ping, string>' (Phase.Default, order 0); RunsAfter and RunsBefore only order behaviors that have the same phase and order",
                         ]
                     );
    }

    [Test]
    [MethodDataSource(nameof(Forms))]
    public async Task KRX008_A_Cycle_Through_A_Closing_Of_A_Generic_Behavior(OrderingForm form, CancellationToken cancellationToken)
    {
        var diagnostics = await Krx008Async(
            form,
            Behavior("A", Filler + "{{After:G<Ping, string>}}")
          + """
            [RunsAfter(typeof(A))]
            public sealed class G<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
            {
                public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse> =>
                    next.InvokeAsync(cancellationToken);
            }
            """,
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX008"))
                    .IsEquivalentTo(["'A' is part of a RunsAfter/RunsBefore cycle between 'A', 'G<TRequest, TResponse>', so no order satisfies them"]);
    }

    [Test]
    [MethodDataSource(nameof(Forms))]
    public async Task KRX008_Edges_That_Agree_Or_Name_No_Behavior_Report_Nothing(OrderingForm form, CancellationToken cancellationToken)
    {
        var diagnostics = await Krx008Async(
            form,
            Behavior("Inner", "[BehaviorOrder(Phase.Transaction)]\n{{After:Outer}}\n{{After:string}}")
          + Behavior("Outer", "[BehaviorOrder(Phase.Observability)]\n{{Before:Inner}}\n{{Before:Third}}")
          + Behavior("Third", "[BehaviorOrder(Phase.Transaction)]\n{{After:Open<Ping, string>}}")
          + Open,
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    public static IEnumerable<Func<OrderingForm>> Forms() => [() => OrderingForm.TypeOf, () => OrderingForm.Generic, () => OrderingForm.Mixed];

    private Task<ImmutableArray<Diagnostic>> Krx008Async(OrderingForm form, string behaviors, CancellationToken cancellationToken) =>
        AnalyzeAsync<BehaviorAnalyzer>(Requests + OrderingForms.Expand(behaviors, form), cancellationToken);

    // KRX009 -----------------------------------------------------------------------------------------------------------------------------

    [Test]
    public async Task KRX009_Attached_Behaviors_That_Do_Not_Fit(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(
            Requests
          + Open
          + """
            public sealed class CommandsOnly<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>, ICommand
            {
                public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse> =>
                    next.InvokeAsync(cancellationToken);
            }

            public sealed class ForCount : IRequestPipelineBehavior<Count, int>
            {
                public ValueTask<int> Handle<TNext>(Count request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<int> =>
                    next.InvokeAsync(cancellationToken);
            }

            public sealed class OnPublish<TNotification> : INotificationPipelineBehavior<TNotification> where TNotification : INotification
            {
                public ValueTask Handle<TNext>(TNotification notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext =>
                    next.InvokeAsync(cancellationToken);
            }

            public sealed class WrapNames : IStreamPipelineBehavior<Names, string>
            {
                public IAsyncEnumerable<string> Handle<TNext>(Names request, TNext next, CancellationToken cancellationToken) where TNext : struct, IStreamNext<string> =>
                    next.InvokeAsync(cancellationToken);
            }

            public static partial class PingHandler
            {
                [Behavior(typeof(CommandsOnly<,>))]
                [Behavior(typeof(ForCount))]
                [Behavior(typeof(WrapNames))]
                [Behavior(typeof(string))]
                [Behavior(typeof(Open<,>))]
                private static string Handle(Ping request) => "";
            }

            public static partial class NamesHandler
            {
                [Behavior(typeof(Open<,>))]
                [Behavior(typeof(WrapNames))]
                private static IAsyncEnumerable<string> Handle(Names request) => null!;
            }

            public static partial class PlacedHandler
            {
                [Behavior(typeof(OnPublish<>))]
                private static ValueTask Handle(Placed notification) => default;
            }

            [Behavior(typeof(Open<,>))]
            [Behavior(typeof(OnPublish<>))]
            public sealed record Shipped : INotification;
            """,
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX009"))
                    .IsEquivalentTo(
                         [
                             "Behavior 'CommandsOnly<,>' does not fit 'PingHandler.Handle(Ping)': 'Ping' does not satisfy the constraint 'TRequest : ICommand'",
                             "Behavior 'ForCount' does not fit 'PingHandler.Handle(Ping)': it is closed over 'IRequestPipelineBehavior<Count, int>', but the target needs exactly 'IRequestPipelineBehavior<Ping, string>'",
                             "Behavior 'Open<,>' does not fit 'NamesHandler.Handle(Names)': it is a request behavior (IRequestPipelineBehavior), but the target needs IStreamPipelineBehavior<TRequest, TItem> or IStreamHookBehavior<TRequest, TItem>",
                             "Behavior 'Open<,>' does not fit 'Shipped': it is a request behavior (IRequestPipelineBehavior), but the target needs INotificationPipelineBehavior<TNotification>",
                             "Behavior 'OnPublish<>' does not fit 'PlacedHandler.Handle(Placed)': it is a per-publish notification behavior (INotificationPipelineBehavior), but the target needs INotificationHandlerPipelineBehavior<TNotification>",
                             "Behavior 'WrapNames' does not fit 'PingHandler.Handle(Ping)': it is a stream wrap behavior (IStreamPipelineBehavior), but the target needs IRequestPipelineBehavior<TRequest, TResponse>",
                             "Behavior 'string' does not fit 'PingHandler.Handle(Ping)': it is not a Keryx behavior",
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
            public sealed class ValueItems<TRequest, TItem> : IStreamPipelineBehavior<TRequest, TItem> where TRequest : IStreamRequest<TItem> where TItem : struct
            {
                public IAsyncEnumerable<TItem> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, IStreamNext<TItem> =>
                    next.InvokeAsync(cancellationToken);
            }

            public sealed class Commands<TRequest, TItem> : IStreamHookBehavior<TRequest, TItem> where TRequest : IStreamRequest<TItem>, ICommand
            {
                public ValueTask OnStartAsync(TRequest request, CancellationToken cancellationToken) => default;
                public bool OnItem(TRequest request, ref TItem item) => true;
                public ValueTask OnCompleteAsync(TRequest request) => default;
                public ValueTask OnErrorAsync(TRequest request, Exception exception) => default;
            }

            public sealed class CommandItems<TRequest, TItem> : IStreamPipelineBehavior<TRequest, TItem> where TRequest : IStreamRequest<TItem>, ICommand
            {
                public IAsyncEnumerable<TItem> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, IStreamNext<TItem> =>
                    next.InvokeAsync(cancellationToken);
            }

            public sealed class ValueHooks<TRequest, TItem> : IStreamHookBehavior<TRequest, TItem> where TRequest : IStreamRequest<TItem> where TItem : struct
            {
                public ValueTask OnStartAsync(TRequest request, CancellationToken cancellationToken) => default;
                public bool OnItem(TRequest request, ref TItem item) => true;
                public ValueTask OnCompleteAsync(TRequest request) => default;
                public ValueTask OnErrorAsync(TRequest request, Exception exception) => default;
            }

            public sealed record Ticks : IStreamRequest<int>, ICommand;

            public static partial class NamesHandler
            {
                [Behavior(typeof(ValueItems<,>))]
                [Behavior(typeof(Commands<,>))]
                [Behavior(typeof(CommandItems<,>))]
                [Behavior(typeof(ValueHooks<,>))]
                private static IAsyncEnumerable<string> Handle(Names request) => null!;
            }

            public static partial class TicksHandler
            {
                [Behavior(typeof(ValueItems<,>))]
                [Behavior(typeof(Commands<,>))]
                [Behavior(typeof(CommandItems<,>))]
                [Behavior(typeof(ValueHooks<,>))]
                private static IAsyncEnumerable<int> Handle(Ticks request) => null!;
            }
            """,
            cancellationToken
        );

        // Each kind is checked on both type arguments: wrap behaviors and hooks on TRequest and on TItem.
        await Assert.That(Messages(diagnostics, "KRX009"))
                    .IsEquivalentTo(
                         [
                             "Behavior 'Commands<,>' does not fit 'NamesHandler.Handle(Names)': 'Names' does not satisfy the constraint 'TRequest : ICommand'",
                             "Behavior 'ValueItems<,>' does not fit 'NamesHandler.Handle(Names)': 'string' does not satisfy the constraint 'TItem : struct'",
                             "Behavior 'CommandItems<,>' does not fit 'NamesHandler.Handle(Names)': 'Names' does not satisfy the constraint 'TRequest : ICommand'",
                             "Behavior 'ValueHooks<,>' does not fit 'NamesHandler.Handle(Names)': 'string' does not satisfy the constraint 'TItem : struct'",
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

            public sealed class Nested<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>, Outer<TResponse>.IMarker
            {
                public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse> =>
                    next.InvokeAsync(cancellationToken);
            }

            public sealed class NestedGeneric<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>, Outer<TResponse>.IInner<TRequest>
            {
                public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse> =>
                    next.InvokeAsync(cancellationToken);
            }

            public static partial class PingHandler
            {
                [Behavior(typeof(Nested<,>))]
                [Behavior(typeof(NestedGeneric<,>))]
                private static string Handle(Ping request) => "";
            }

            public static partial class MarkedHandler
            {
                [Behavior(typeof(Nested<,>))]
                [Behavior(typeof(NestedGeneric<,>))]
                private static string Handle(Marked request) => "";
            }
            """,
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX009"))
                    .IsEquivalentTo(
                         [
                             "Behavior 'Nested<,>' does not fit 'PingHandler.Handle(Ping)': 'Ping' does not satisfy the constraint 'TRequest : Outer<string>.IMarker'",
                             "Behavior 'NestedGeneric<,>' does not fit 'PingHandler.Handle(Ping)': 'Ping' does not satisfy the constraint 'TRequest : Outer<string>.IInner<Ping>'",
                         ]
                     );
        await Assert.That(Ids(diagnostics).Distinct()).IsEquivalentTo(["KRX009"]);
    }

    // KRX019, KRX020, KRX018 -------------------------------------------------------------------------------------------------------------

    [Test]
    [Arguments("public sealed class X : IRequestPipelineBehavior<Ping, string>, IRequestPipelineBehavior<Count, int> { public ValueTask<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<string> => default; public ValueTask<int> Handle<TNext>(Count r, TNext n, CancellationToken c) where TNext : struct, INext<int> => default; }", "implements more than one Keryx behavior interface closing (IRequestPipelineBehavior<Ping, string>, IRequestPipelineBehavior<Count, int>); a behavior implements exactly one")]
    [Arguments("public abstract class X : IRequestPipelineBehavior<Ping, string> { public ValueTask<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<string> => default; }", "is abstract; generated code constructs it and calls it on its concrete type")]
    [Arguments("public sealed class X<TResponse, TRequest> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse> { public ValueTask<TResponse> Handle<TNext>(TRequest r, TNext n, CancellationToken c) where TNext : struct, INext<TResponse> => default; }", "implements 'IRequestPipelineBehavior<TRequest, TResponse>' with type parameters that are not the interface's, in order; a generic behavior declares exactly the interface's type parameters (as in 'X<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>'), or is closed and non-generic")]
    [Arguments("public sealed class X<T> : IRequestPipelineBehavior<Ping, T> where T : struct { public ValueTask<T> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<T> => default; }", "implements 'IRequestPipelineBehavior<Ping, T>' with type parameters that are not the interface's, in order; a generic behavior declares exactly the interface's type parameters (as in 'X<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>'), or is closed and non-generic")]
    [Arguments("public class X : IRequestPipelineBehavior<Ping, string> { public virtual ValueTask<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<string> => default; }", "implements 'Handle' with a method that is 'virtual'; generated code calls it on the concrete type, which must be a direct (non-virtual) call")]
    [Arguments("public abstract class B { public abstract ValueTask<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<string>; } public sealed class X : B, IRequestPipelineBehavior<Ping, string> { public override ValueTask<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) => default; }", "implements 'Handle' with a method that is 'override'; generated code calls it on the concrete type, which must be a direct (non-virtual) call")]
    [Arguments("public sealed class X : IRequestPipelineBehavior<Ping, string> { ValueTask<string> IRequestPipelineBehavior<Ping, string>.Handle<TNext>(Ping r, TNext n, CancellationToken c) => default; }", "implements 'Handle' explicitly; generated code calls it on the concrete type, so implement it as a public method")]
    [Arguments("public struct X : IRequestPipelineBehavior<Ping, string> { public ValueTask<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<string> => default; }", "is a struct; a behavior is a class, resolved once per scope and held by the pipeline context")]
    [Arguments("public sealed class Outer<T> { public sealed class X : IRequestPipelineBehavior<Ping, string> { public ValueTask<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<string> => default; } }", "is nested in a generic type; a behavior is open with the interface's type parameters, or closed and non-generic")]
    [Arguments("public sealed class X : IStreamPipelineBehavior<Names, string>, IStreamHookBehavior<Names, string> { public IAsyncEnumerable<string> Handle<TNext>(Names r, TNext n, CancellationToken c) where TNext : struct, IStreamNext<string> => n.InvokeAsync(c); public ValueTask OnStartAsync(Names r, CancellationToken c) => default; public bool OnItem(Names r, ref string i) => true; public ValueTask OnCompleteAsync(Names r) => default; public ValueTask OnErrorAsync(Names r, Exception e) => default; }", "implements more than one Keryx behavior interface closing (IStreamPipelineBehavior<Names, string>, IStreamHookBehavior<Names, string>); a behavior implements exactly one")]
    [Arguments("public abstract class X : IStreamPipelineBehavior<Names, string> { public IAsyncEnumerable<string> Handle<TNext>(Names r, TNext n, CancellationToken c) where TNext : struct, IStreamNext<string> => n.InvokeAsync(c); }", "is abstract; generated code constructs it and calls it on its concrete type")]
    [Arguments("public class X : IStreamPipelineBehavior<Names, string> { public virtual IAsyncEnumerable<string> Handle<TNext>(Names r, TNext n, CancellationToken c) where TNext : struct, IStreamNext<string> => n.InvokeAsync(c); }", "implements 'Handle' with a method that is 'virtual'; generated code calls it on the concrete type, which must be a direct (non-virtual) call")]
    [Arguments("public abstract class X : IStreamPipelineBehavior<Names, string> { public abstract IAsyncEnumerable<string> Handle<TNext>(Names r, TNext n, CancellationToken c) where TNext : struct, IStreamNext<string>; }", "implements 'Handle' with a method that is 'abstract'; generated code calls it on the concrete type, which must be a direct (non-virtual) call")]
    [Arguments("public abstract class B { public abstract IAsyncEnumerable<string> Handle<TNext>(Names r, TNext n, CancellationToken c) where TNext : struct, IStreamNext<string>; } public sealed class X : B, IStreamPipelineBehavior<Names, string> { public override IAsyncEnumerable<string> Handle<TNext>(Names r, TNext n, CancellationToken c) => n.InvokeAsync(c); }", "implements 'Handle' with a method that is 'override'; generated code calls it on the concrete type, which must be a direct (non-virtual) call")]
    [Arguments("public abstract class X : IStreamHookBehavior<Names, string> { public ValueTask OnStartAsync(Names r, CancellationToken c) => default; public bool OnItem(Names r, ref string i) => true; public ValueTask OnCompleteAsync(Names r) => default; public ValueTask OnErrorAsync(Names r, Exception e) => default; }", "is abstract; generated code constructs it and calls it on its concrete type")]
    [Arguments("public class X : IStreamHookBehavior<Names, string> { public virtual ValueTask OnStartAsync(Names r, CancellationToken c) => default; public bool OnItem(Names r, ref string i) => true; public ValueTask OnCompleteAsync(Names r) => default; public ValueTask OnErrorAsync(Names r, Exception e) => default; }", "implements 'OnStartAsync' with a method that is 'virtual'; generated code calls it on the concrete type, which must be a direct (non-virtual) call")]
    public async Task KRX019_Unsupported_Behavior_Shapes(string behavior, string reason, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(Requests + behavior, cancellationToken);

        await Assert.That(Ids(diagnostics).Distinct()).IsEquivalentTo(["KRX019"]);
        await Assert.That(Messages(diagnostics, "KRX019")).Contains(m => m.EndsWith(reason, StringComparison.Ordinal));
    }

    [Test]
    [Arguments("public sealed class X : IStreamPipelineBehavior<Names, string> { public IAsyncEnumerable<string> Handle<TNext>(Names r, TNext n, CancellationToken c) where TNext : struct, IStreamNext<string> => n.InvokeAsync(c); }")]
    [Arguments("public sealed class X<TRequest, TItem> : IStreamPipelineBehavior<TRequest, TItem> where TRequest : IStreamRequest<TItem> { public IAsyncEnumerable<TItem> Handle<TNext>(TRequest r, TNext n, CancellationToken c) where TNext : struct, IStreamNext<TItem> => n.InvokeAsync(c); }")]
    [Arguments("public sealed class X : IStreamHookBehavior<Names, string> { public ValueTask OnStartAsync(Names r, CancellationToken c) => default; public bool OnItem(Names r, ref string i) => true; public ValueTask OnCompleteAsync(Names r) => default; public ValueTask OnErrorAsync(Names r, Exception e) => default; }")]
    [Arguments("public sealed class X<TRequest, TItem> : IStreamHookBehavior<TRequest, TItem> where TRequest : IStreamRequest<TItem> { public ValueTask OnStartAsync(TRequest r, CancellationToken c) => default; public bool OnItem(TRequest r, ref TItem i) => true; public ValueTask OnCompleteAsync(TRequest r) => default; public ValueTask OnErrorAsync(TRequest r, Exception e) => default; }")]
    public async Task KRX019_A_Sealed_Stream_Behavior_With_One_Interface_Reports_Nothing(string behavior, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(Requests + behavior, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    [Arguments("public sealed class X(int a) : IRequestPipelineBehavior<Ping, string> { public X() : this(1) { } public ValueTask<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<string> => default; }", "has 2 public constructors; the host constructs each behavior through exactly one")]
    [Arguments("public sealed class X : IRequestPipelineBehavior<Ping, string> { private X() { } public ValueTask<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<string> => default; }", "has no public constructor; the host constructs each behavior through its one public constructor")]
    [Arguments("public sealed class X(ref int a) : IRequestPipelineBehavior<Ping, string> { public ValueTask<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<string> => default; }", "has a constructor parameter 'a' that is passed by reference; constructor parameters are injected by value")]
    public async Task KRX020_Behavior_Constructors(string behavior, string reason, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(Requests + behavior, cancellationToken);

        await Assert.That(Messages(diagnostics, "KRX020")).IsEquivalentTo([$"Behavior 'X' {reason}"]);
        await Assert.That(Ids(diagnostics).Distinct()).IsEquivalentTo(["KRX020"]);
    }

    [Test]
    public async Task KRX018_Behavior_Constructor_Keys(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(
            Requests
          + """
            public sealed class X([FromKeyedServices] string a) : IRequestPipelineBehavior<Ping, string>
            {
                public ValueTask<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<string> => default;
            }
            """,
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX018"]);
    }

    [Test]
    public async Task Valid_Behaviors_Report_Nothing(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(
            Requests
          + Open
          + """
            public interface IAudit { }

            [GlobalBehavior]
            [BehaviorOrder(Phase.Observability, Order = 2)]
            public sealed class Audit<TRequest, TResponse>(IAudit audit, [FromKeyedServices("k")] IAudit? keyed = null) : IRequestPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
            {
                public async ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse> =>
                    await next.InvokeAsync(cancellationToken);
            }

            public sealed class Hook : IStreamHookBehavior<Names, string>
            {
                public ValueTask OnStartAsync(Names request, CancellationToken cancellationToken) => default;
                public bool OnItem(Names request, ref string item) => true;
                public ValueTask OnCompleteAsync(Names request) => default;
                public ValueTask OnErrorAsync(Names request, Exception exception) => default;
            }

            public sealed class Each<TNotification> : INotificationHandlerPipelineBehavior<TNotification> where TNotification : INotification
            {
                public ValueTask Handle<TNext>(TNotification notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext =>
                    next.InvokeAsync(cancellationToken);
            }

            public abstract class Base { public ValueTask<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<string> => n.InvokeAsync(c); }
            public sealed class Inherited : Base, IRequestPipelineBehavior<Ping, string> { }

            public static partial class PingHandler
            {
                [Behavior(typeof(Open<,>))]
                [Behavior(typeof(Open<Ping, string>))]
                [Behavior(typeof(Audit<,>))]
                [Behavior(typeof(Inherited))]
                private static string Handle(Ping request) => "";
            }

            public static partial class NamesHandler
            {
                [Behavior(typeof(Hook))]
                private static IAsyncEnumerable<string> Handle(Names request) => null!;
            }

            public static partial class AnyHandler
            {
                [Behavior(typeof(Each<>))]
                private static ValueTask Handle(INotification notification) => default;
            }
            """,
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    // KRX042, KRX016 ---------------------------------------------------------------------------------------------------------------------

    [Test]
    public async Task KRX042_Behavior_Attributes_Without_Effect(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(
            Requests
          + Open
          + """
            [Behavior(typeof(Open<,>))]
            public sealed class NotANotification { [Behavior(typeof(Open<,>))] public void Handle(int value) { } }

            [GlobalBehavior]
            public sealed class NotABehavior { }
            """,
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX042"))
                    .IsEquivalentTo(
                         [
                             "[Behavior] on 'NotANotification' has no effect, because it is not a notification type; attach request and stream behaviors to the handler method",
                             "[Behavior] on 'NotANotification.Handle(int)' has no effect, because it is not a handler",
                             "[GlobalBehavior] on 'NotABehavior' has no effect, because it does not implement exactly one Keryx behavior interface closing",
                         ]
                     );
        await Assert.That(Ids(diagnostics).Distinct()).IsEquivalentTo(["KRX042"]);
    }

    [Test]
    public async Task KRX016_A_Host_Behavior_Narrower_Than_Internal(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<BehaviorAnalyzer>(
            "[assembly: KeryxHost]\n"
          + Requests
          + """
            public static partial class PingHandler
            {
                [Behavior(typeof(Hidden))]
                private static string Handle(Ping request) => "";

                private sealed class Hidden : IRequestPipelineBehavior<Ping, string>
                {
                    public ValueTask<string> Handle<TNext>(Ping r, TNext n, CancellationToken c) where TNext : struct, INext<string> => n.InvokeAsync(c);
                }
            }

            internal static class Outer
            {
                [GlobalBehavior]
                private sealed class HiddenGlobal<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
                {
                    public ValueTask<TResponse> Handle<TNext>(TRequest r, TNext n, CancellationToken c) where TNext : struct, INext<TResponse> => n.InvokeAsync(c);
                }

                [GlobalBehavior]
                internal sealed class Reachable<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
                {
                    public ValueTask<TResponse> Handle<TNext>(TRequest r, TNext n, CancellationToken c) where TNext : struct, INext<TResponse> => n.InvokeAsync(c);
                }
            }
            """,
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX016"))
                    .IsEquivalentTo(
                         [
                             "'Outer.HiddenGlobal<TRequest, TResponse>' must be at least internal, because it is a global behavior and the host's generated mediator and AddKeryx() use it from outside its containing type",
                             "'PingHandler.Hidden' must be at least internal, because 'PingHandler.Handle(Ping)' attaches it and the host's generated mediator and AddKeryx() use it from outside its containing type",
                         ]
                     );
    }

    [Test]
    [MethodDataSource(nameof(Forms))]
    public async Task Diagnostics_Point_At_The_Attribute(OrderingForm form, CancellationToken cancellationToken)
    {
        var diagnostics = await Krx008Async(
            form,
            Behavior("Inner", "[BehaviorOrder(Phase.Observability)]\n" + Filler + "{{After:Outer}}") + Behavior("Outer", "[BehaviorOrder(Phase.Transaction)]"),
            cancellationToken
        );

        // Under Mixed, the filler takes the typeof form and the reported edge the generic one.
        var diagnostic = diagnostics.Single();
        await Assert.That(diagnostic.Location.SourceTree!.GetText(cancellationToken).ToString(diagnostic.Location.SourceSpan))
                    .IsEqualTo(form == OrderingForm.TypeOf ? "RunsAfter(typeof(Outer))" : "RunsAfter<Outer>");
        await Assert.That(diagnostic.Descriptor.CustomTags).Contains(WellKnownDiagnosticTags.CompilationEnd);
        await Assert.That(string.Create(CultureInfo.InvariantCulture, $"{diagnostic.Severity}")).IsEqualTo("Error");
    }
}
