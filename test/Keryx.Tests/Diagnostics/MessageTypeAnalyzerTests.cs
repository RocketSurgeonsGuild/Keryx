using Keryx.Analyzers.Diagnostics;

namespace Keryx.Tests.Diagnostics;

/// <summary>
///     KRX011, KRX012 (types), KRX016 (types, responses and the behaviors attached to notification types, the assembly or marked global)
///     and KRX041, each with a positive and a negative case.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class MessageTypeAnalyzerTests : AnalyzerTest
{
    [Test]
    [Arguments("public sealed class Both : IRequest<int>, IRequest<string> { }")]
    [Arguments("public sealed class Both : IRequest<int>, IStreamRequest<int> { }")]
    [Arguments("public sealed class Both : IRequest<int>, INotification { }")]
    [Arguments("public abstract class Base : IRequest<int> { } public sealed class Both : Base, IRequest<string> { }")]
    public async Task KRX011_Conflicting_Interfaces(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<MessageTypeAnalyzer>(source, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX011"]);
    }

    [Test]
    [Arguments("public abstract class Base : IRequest<int> { } public sealed class Derived : Base { }")]
    [Arguments("public interface IQuery : IRequest<int>, IStreamRequest<int> { }")]
    [Arguments("public sealed class Unit2 : IRequest { }")]
    public async Task KRX011_One_Closing(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<MessageTypeAnalyzer>(source, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    [Arguments("public sealed class Get<T> : IRequest<T> { }")]
    [Arguments("public sealed class Outer<T> { public sealed class Get : IRequest<int> { } }")]
    [Arguments("public ref struct Get : IRequest<int> { }")]
    [Arguments("public sealed class Happened<T> : INotification { }")]
    public async Task KRX012_Unsupported_Shape(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<MessageTypeAnalyzer>(source, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX012"]);
    }

    [Test]
    public async Task KRX012_Generic_Abstract_Base_With_Closed_Subtypes(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<MessageTypeAnalyzer>(
            "public abstract class GetById<T> : IRequest<T> { } public sealed class GetOrder : GetById<string> { }",
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    [Arguments("internal sealed class Hidden : IRequest<int> { }")]
    [Arguments("public static class Outer { internal sealed class Hidden : INotification { } }")]
    [Arguments("internal sealed class Result { } public sealed class Get : IRequest<Result> { }")]
    [Arguments("internal sealed class Result { } public sealed class Get : IRequest<List<Result>> { }")]
    [Arguments("internal sealed class Item { } public sealed class List : IStreamRequest<Item> { }")]
    public async Task KRX016_Not_Public_Outside_A_Host(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<MessageTypeAnalyzer>(source, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX016"]);
    }

    [Test]
    [Arguments(true, "internal sealed class Hidden : IRequest<int> { }")]
    [Arguments(false, "public sealed class Get : IRequest<List<string>> { }")]
    public async Task KRX016_Public_Or_In_A_Host(bool host, string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<MessageTypeAnalyzer>(source, cancellationToken, host: host);

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    /// <summary>A request behavior method, public.</summary>
    private const string RequestMethod =
        "{ public static ValueTask<TR> Handle<TQ, TR, TN>(TQ q, TN n) where TQ : IRequest<TR> where TN : struct, INext<TR> => n.InvokeAsync(default); }";

    /// <summary>A notification behavior method, public.</summary>
    private const string NotificationMethod =
        "{ public static ValueTask Handle<T, TN>(T m, TN n) where T : INotification where TN : struct, INext => n.InvokeAsync(default); }";

    /// <summary>A notification behavior method, internal.</summary>
    private const string InternalNotificationMethod =
        "{ internal static ValueTask Handle<T, TN>(T m, TN n) where T : INotification where TN : struct, INext => n.InvokeAsync(default); }";

    [Test]
    [Arguments("[NotificationBehavior] internal static class Hidden " + NotificationMethod + " [Behavior(typeof(Hidden))] public sealed class Placed : INotification { }")]
    [Arguments("[NotificationBehavior] public static class Hidden " + InternalNotificationMethod + " [Behavior(typeof(Hidden))] public readonly struct Tick : INotification { }")]
    [Arguments("[assembly: Behavior(typeof(Hidden))] [RequestBehavior] internal static class Hidden " + RequestMethod)]
    [Arguments("[RequestBehavior] [GlobalBehavior] internal static class Hidden " + RequestMethod)]
    [Arguments("public static class Outer { [RequestBehavior] [GlobalBehavior] internal static class Hidden " + RequestMethod + " }")]
    [Arguments("[NotificationBehavior] public static class Hidden { [GlobalBehavior] internal static ValueTask Handle<T, TN>(T m, TN n) where T : INotification where TN : struct, INext => n.InvokeAsync(default); }")]
    public async Task KRX016_Behavior_Not_Public_Outside_A_Host(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<MessageTypeAnalyzer>(source, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX016"]);
    }

    [Test]
    [Arguments(true, "[NotificationBehavior] internal static class Hidden " + NotificationMethod + " [Behavior(typeof(Hidden))] public sealed class Placed : INotification { }")]
    [Arguments(true, "[assembly: Behavior(typeof(Hidden))] [RequestBehavior] internal static class Hidden " + RequestMethod)]
    [Arguments(true, "[RequestBehavior] [GlobalBehavior] internal static class Hidden " + RequestMethod)]
    [Arguments(false, "[NotificationBehavior] public static class Visible " + NotificationMethod + " [Behavior(typeof(Visible))] public sealed class Placed : INotification { }")]
    [Arguments(false, "[assembly: Behavior(typeof(Visible))] [RequestBehavior] public static class Visible " + RequestMethod)]
    [Arguments(false, "[RequestBehavior] [GlobalBehavior] public static class Visible " + RequestMethod)]
    [Arguments(false, "internal sealed class NotABehavior { } [Behavior(typeof(NotABehavior))] public sealed class Placed : INotification { }")]
    public async Task KRX016_Behavior_Public_Or_In_A_Host(bool host, string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<MessageTypeAnalyzer>(source, cancellationToken, host: host);

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task KRX016_Behavior_Messages(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<MessageTypeAnalyzer>(
            """
            [assembly: Behavior(typeof(Sample.Hidden))]

            namespace Sample;

            [RequestBehavior]
            [NotificationBehavior]
            internal static class Hidden
            {
                public static ValueTask<TR> Handle<TQ, TR, TN>(TQ q, TN n) where TQ : IRequest<TR> where TN : struct, INext<TR> => n.InvokeAsync(default);
                public static ValueTask Handle<T, TN>(T m, TN n) where T : INotification where TN : struct, INext => n.InvokeAsync(default);
            }

            [Behavior(typeof(Hidden))] public sealed class Placed : INotification { }

            [RequestBehavior]
            [GlobalBehavior]
            internal static class Everywhere
            {
                public static ValueTask<TR> Handle<TQ, TR, TN>(TQ q, TN n) where TQ : IRequest<TR> where TN : struct, INext<TR> => n.InvokeAsync(default);
            }
            """,
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX016"))
                    .IsEquivalentTo(
                         [
                             "'Everywhere' must be public, because it is a global behavior, every host that references this assembly applies it, and this assembly is not a host",
                             "'Hidden' must be public, because the notification 'Placed' attaches it and this assembly is not a host",
                             "'Hidden' must be public, because this assembly attaches it to its handlers and is not a host",
                         ]
                     );
    }

    [Test]
    [Arguments("[PublishStrategy(PublishStrategy.Parallel)] public sealed class Get : IRequest<int> { }")]
    [Arguments("[PublishStrategy(PublishStrategy.Parallel)] public sealed class Plain { }")]
    [Arguments("[PublishStrategy(PublishStrategy.Parallel)] public abstract class Event : INotification { }")]
    public async Task KRX041_PublishStrategy_Not_On_A_Notification(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<MessageTypeAnalyzer>(source, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX041"]);
    }

    [Test]
    [Arguments("[PublishStrategy(PublishStrategy.Parallel)] public sealed class Placed : INotification { }")]
    [Arguments("[PublishStrategy(PublishStrategy.Sequential)] public readonly struct Tick : INotification { }")]
    public async Task KRX041_PublishStrategy_On_A_Notification(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<MessageTypeAnalyzer>(source, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task Reports_Every_Rule_With_Its_Message(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<MessageTypeAnalyzer>(
            """
            namespace Sample;

            public sealed class Both : IRequest<int>, IStreamRequest<int>, INotification { }
            public sealed class Get<T> : IRequest<T> { }
            public ref struct Span : IRequest<int> { }
            internal sealed class Hidden : IRequest<Result> { }
            internal sealed class Result { }
            [PublishStrategy(PublishStrategy.Parallel)] public sealed class NotANotification { }
            """,
            cancellationToken
        );

        await Verify(Sorted(diagnostics));
    }
}
