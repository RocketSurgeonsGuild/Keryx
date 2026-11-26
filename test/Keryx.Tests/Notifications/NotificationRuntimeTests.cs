using System.Globalization;
using System.Reflection;
using Keryx.Tests.Hosts;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions.Enums;

namespace Keryx.Tests.Notifications;

/// <summary>
///     Compiles a host with notification handlers and publishes through it (ADR 0001, Notifications; the Phase 6 brief): zero, one and many
///     handlers, polymorphic handlers (interface, base class, <c>INotification</c>), <c>[HandlerOrder]</c>, both strategies, failures in
///     several handlers (always one <c>AggregateException</c>, in handler order), cancellation, struct notifications on the typed and generic
///     paths, and runtime-type dispatch. Each scenario runs inside the compiled host (<c>Sample.Probe</c>), which returns what it logged.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class NotificationRuntimeTests : HostTest
{
    internal const string Source = """
        namespace Sample;

        public static class Log
        {
            private static readonly List<string> Entries = [];

            public static void Write(string entry)
            {
                lock (Entries)
                {
                    Entries.Add(entry);
                }
            }

            public static string[] Take()
            {
                lock (Entries)
                {
                    var entries = Entries.ToArray();
                    Entries.Clear();
                    return entries;
                }
            }
        }

        public interface IJournal { string Prefix { get; } }
        public sealed class Journal : IJournal { public string Prefix => "journal"; }

        public interface IDomainEvent : INotification { string Name { get; } }

        public sealed record OrderPlaced(string Name) : IDomainEvent;
        public record Shipped(string Name) : IDomainEvent;
        public sealed record ExpressShipped(string Name) : Shipped(Name);
        public readonly record struct Tick(int Value) : IDomainEvent { public string Name => "tick " + Value; }
        public sealed record Failing : INotification;
        public sealed record OneFailure : INotification;
        public sealed record Cancellable : INotification;

        [PublishStrategy(PublishStrategy.Parallel)]
        public sealed record Fanout(TaskCompletionSource Gate) : INotification;

        [PublishStrategy(PublishStrategy.Parallel)]
        public sealed record FanoutFailing : INotification;

        [PublishStrategy(PublishStrategy.Parallel)]
        public sealed record CancellableParallel : INotification;

        // OrderPlaced: AuditDomainEvents (-1), OnOrderPlacedA and OnOrderPlacedB (0, by class name), OnOrderPlacedLast (10), CatchAll (100).
        public static partial class OnOrderPlacedB
        {
            private static ValueTask Handle(OrderPlaced notification)
            {
                Log.Write("B " + notification.Name);
                return default;
            }
        }

        public static partial class OnOrderPlacedA
        {
            private static Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
            {
                Log.Write("A " + notification.Name);
                return Task.CompletedTask;
            }
        }

        public static partial class OnOrderPlacedLast
        {
            [HandlerOrder(10)]
            private static async ValueTask Handle(OrderPlaced notification)
            {
                await Task.Yield();
                Log.Write("last " + notification.Name);
            }
        }

        public partial class AuditDomainEvents
        {
            [HandlerOrder(-1)]
            private static ValueTask Handle(IDomainEvent notification, IJournal journal)
            {
                Log.Write(journal.Prefix + " " + notification.GetType().Name + " " + notification.Name);
                return default;
            }
        }

        public static partial class CatchAll
        {
            [HandlerOrder(100)]
            private static ValueTask Handle(INotification notification)
            {
                Log.Write("all " + notification.GetType().Name);
                return default;
            }
        }

        public static partial class OnShipped
        {
            private static ValueTask Handle(Shipped notification)
            {
                Log.Write("shipped " + notification.GetType().Name);
                return default;
            }
        }

        public static partial class OnExpressShipped
        {
            [HandlerOrder(1)]
            private static ValueTask Handle(ExpressShipped notification)
            {
                Log.Write("express");
                return default;
            }
        }

        public static partial class OnTick
        {
            private static ValueTask Handle(Tick notification)
            {
                Log.Write("tick " + notification.Value);
                return default;
            }
        }

        public static partial class Failing1
        {
            [HandlerOrder(1)]
            private static ValueTask Handle(Failing notification) => throw new InvalidOperationException("sync 1");
        }

        public static partial class Failing2
        {
            [HandlerOrder(2)]
            private static async ValueTask Handle(Failing notification)
            {
                await Task.Yield();
                throw new InvalidOperationException("async 2");
            }
        }

        public static partial class Failing3
        {
            [HandlerOrder(3)]
            private static ValueTask Handle(Failing notification)
            {
                Log.Write("3 ran");
                return default;
            }
        }

        public static partial class Failing4
        {
            [HandlerOrder(4)]
            private static ValueTask Handle(Failing notification) => ValueTask.FromException(new InvalidOperationException("faulted 4"));
        }

        public static partial class OnOneFailure
        {
            private static ValueTask Handle(OneFailure notification) => throw new InvalidOperationException("only");
        }

        public static partial class Cancellable1
        {
            [HandlerOrder(1)]
            private static ValueTask Handle(Cancellable notification, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return default;
            }
        }

        public static partial class Cancellable2
        {
            [HandlerOrder(2)]
            private static ValueTask Handle(Cancellable notification, CancellationToken cancellationToken)
            {
                Log.Write("2 saw cancelled " + cancellationToken.IsCancellationRequested);
                return default;
            }
        }

        public static partial class Cancellable3
        {
            [HandlerOrder(3)]
            private static Task Handle(Cancellable notification, CancellationToken cancellationToken) => Task.FromCanceled(cancellationToken);
        }

        public static partial class CancellableParallel1
        {
            [HandlerOrder(1)]
            private static async ValueTask Handle(CancellableParallel notification, CancellationToken cancellationToken)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        public static partial class CancellableParallel2
        {
            [HandlerOrder(2)]
            private static Task Handle(CancellableParallel notification, CancellationToken cancellationToken)
            {
                Log.Write("2 saw cancelled " + cancellationToken.IsCancellationRequested);
                return Task.FromCanceled(cancellationToken);
            }
        }

        // OQ-43: a notification behavior's exception propagates as it is.
        [NotificationBehavior]
        public static class ThrowsBeforeNext
        {
            public static ValueTask Handle<T, TNext>(T notification, TNext next, CancellationToken cancellationToken)
                where T : INotification
                where TNext : struct, INext =>
                throw new InvalidOperationException("publish sync");
        }

        [NotificationBehavior]
        public static class ThrowsAfterNext
        {
            public static async ValueTask Handle<T, TNext>(T notification, TNext next, CancellationToken cancellationToken)
                where T : INotification
                where TNext : struct, INext
            {
                await next.InvokeAsync(cancellationToken);
                throw new InvalidOperationException("publish async");
            }
        }

        [NotificationBehavior]
        public static class PassesThrough
        {
            public static async ValueTask Handle<T, TNext>(T notification, TNext next, CancellationToken cancellationToken)
                where T : INotification
                where TNext : struct, INext
            {
                Log.Write("pass>");
                await next.InvokeAsync(cancellationToken);
                Log.Write("<pass");
            }
        }

        [Behavior(typeof(ThrowsBeforeNext))]
        public sealed record PublishThrows : INotification;

        [Behavior(typeof(ThrowsAfterNext))]
        public sealed record PublishThrowsLater : INotification;

        [Behavior(typeof(PassesThrough))]
        public sealed record PublishPassesFailure : INotification;


        public static partial class OnPublishThrows
        {
            private static ValueTask Handle(PublishThrows notification)
            {
                Log.Write("handler ran");
                return default;
            }
        }

        public static partial class OnPublishThrowsLater
        {
            private static ValueTask Handle(PublishThrowsLater notification)
            {
                Log.Write("handler ran");
                return default;
            }
        }

        public static partial class OnPublishPassesFailure
        {
            private static ValueTask Handle(PublishPassesFailure notification) => throw new InvalidOperationException("handler failed");
        }

        // Fanout: the first handler waits for the second, which only a parallel publish can satisfy.
        public static partial class Fanout1
        {
            [HandlerOrder(1)]
            private static async ValueTask Handle(Fanout notification)
            {
                Log.Write("1 start");
                await notification.Gate.Task;
                Log.Write("1 end");
            }
        }

        public static partial class Fanout2
        {
            [HandlerOrder(2)]
            private static ValueTask Handle(Fanout notification)
            {
                Log.Write("2 start");
                notification.Gate.SetResult();
                Log.Write("2 end");
                return default;
            }
        }

        public static partial class FanoutFailing1
        {
            [HandlerOrder(1)]
            private static ValueTask Handle(FanoutFailing notification) => throw new InvalidOperationException("sync 1");
        }

        public static partial class FanoutFailing2
        {
            [HandlerOrder(2)]
            private static async ValueTask Handle(FanoutFailing notification)
            {
                await Task.Yield();
                throw new InvalidOperationException("async 2");
            }
        }

        public static partial class FanoutFailing3
        {
            [HandlerOrder(3)]
            private static ValueTask Handle(FanoutFailing notification)
            {
                Log.Write("3 started");
                return default;
            }
        }

        public static partial class FanoutFailing4
        {
            [HandlerOrder(4)]
            private static Task Handle(FanoutFailing notification) => Task.FromException(new InvalidOperationException("faulted 4"));
        }

        public static class Probe
        {
            public static async Task<string[]> Run(IServiceProvider services, string scenario, object? argument, CancellationToken cancellationToken)
            {
                var mediator = services.GetRequiredService<KeryxMediator>();
                IPublisher publisher = services.GetRequiredService<IPublisher>();
                _ = Log.Take();
                try
                {
                    switch (scenario)
                    {
                        case "typed":
                            await mediator.Publish(new OrderPlaced("o"), cancellationToken);
                            break;
                        case "generic":
                            await publisher.Publish(new OrderPlaced("o"), cancellationToken);
                            break;
                        case "generic as interface":
                            await publisher.Publish<INotification>(new OrderPlaced("o"), cancellationToken);
                            break;
                        case "base class":
                            await mediator.Publish(new Shipped("s"), cancellationToken);
                            break;
                        case "derived through base overload":
                            await mediator.Publish((Shipped)new ExpressShipped("e"), cancellationToken);
                            break;
                        case "struct typed":
                            await mediator.Publish(new Tick(1), cancellationToken);
                            break;
                        case "struct generic":
                            await publisher.Publish(new Tick(2), cancellationToken);
                            break;
                        case "struct boxed by caller":
                            await publisher.Publish<IDomainEvent>(new Tick(3), cancellationToken);
                            break;
                        case "unknown":
                            await publisher.Publish((INotification)argument!, cancellationToken);
                            break;
                        case "failing":
                            await mediator.Publish(new Failing(), cancellationToken);
                            break;
                        case "one failure":
                            await publisher.Publish(new OneFailure(), cancellationToken);
                            break;
                        case "cancellable":
                            await mediator.Publish(new Cancellable(), new CancellationToken(canceled: true));
                            break;
                        case "cancellable parallel":
                            await mediator.Publish(new CancellableParallel(), new CancellationToken(canceled: true));
                            break;
                        case "fanout":
                            await mediator.Publish(new Fanout(new TaskCompletionSource()), cancellationToken);
                            break;
                        case "fanout failing":
                            await publisher.Publish(new FanoutFailing(), cancellationToken);
                            break;
                        case "publish throws":
                            ThrowsFromPublish(() => mediator.Publish(new PublishThrows(), cancellationToken));
                            break;
                        case "publish throws generic":
                            ThrowsFromPublish(() => publisher.Publish(new PublishThrows(), cancellationToken));
                            break;
                        case "publish throws later":
                            await mediator.Publish(new PublishThrowsLater(), cancellationToken);
                            break;
                        case "publish passes failure":
                            await publisher.Publish(new PublishPassesFailure(), cancellationToken);
                            break;
                        case "derived unknown":
                            await mediator.Publish((Shipped)new Hidden("h"), cancellationToken);
                            break;
                        case "null":
                            Null(() => mediator.Publish((OrderPlaced)null!, cancellationToken));
                            Null(() => publisher.Publish<INotification>(null!, cancellationToken));
                            Null(() => publisher.Publish((OrderPlaced)null!, cancellationToken));
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
                    }

                    Log.Write("completed");
                }
                catch (AggregateException exception)
                {
                    var failures = new List<string>();
                    foreach (var inner in exception.InnerExceptions)
                    {
                        failures.Add(inner.GetType().Name + " " + inner.Message);
                    }

                    Log.Write("aggregate: " + string.Join(" | ", failures));
                }
                catch (Exception exception)
                {
                    Log.Write("threw " + exception.GetType().Name + " " + exception.Message);
                }

                return Log.Take();
            }

            /// <summary>Unknown to the host: narrower than internal (OQ-42), although it derives from Shipped, which has handlers.</summary>
            private sealed record Hidden(string Name) : Shipped(Name);

            private static void ThrowsFromPublish(Func<ValueTask> publish)
            {
                try
                {
                    _ = publish();
                    Log.Write("returned a task");
                }
                catch (InvalidOperationException exception)
                {
                    Log.Write("threw synchronously " + exception.Message);
                }
            }

            private static void Null(Func<ValueTask> publish)
            {
                try
                {
                    _ = publish();
                    Log.Write("returned");
                }
                catch (ArgumentNullException)
                {
                    Log.Write("threw synchronously");
                }
            }
        }
        """;

    [Test]
    [Arguments("typed")]
    [Arguments("generic")]
    [Arguments("generic as interface")]
    public async Task Exact_And_Polymorphic_Handlers_Run_In_Handler_Order(string scenario, CancellationToken cancellationToken) =>
        await Assert.That(await RunAsync(scenario, cancellationToken))
                    .IsEquivalentTo(["journal OrderPlaced o", "A o", "B o", "last o", "all OrderPlaced", "completed"], CollectionOrdering.Matching);

    [Test]
    public async Task A_Base_Class_Handler_Receives_The_Base_And_Every_Derived_Notification(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        await Assert.That(await RunAsync(host, "base class", cancellationToken))
                    .IsEquivalentTo(["journal Shipped s", "shipped Shipped", "all Shipped", "completed"], CollectionOrdering.Matching);

        // Statically typed as the base, published through the base's typed overload: dispatch is by runtime type, so the derived type's own
        // handlers run too.
        await Assert.That(await RunAsync(host, "derived through base overload", cancellationToken))
                    .IsEquivalentTo(["journal ExpressShipped e", "shipped ExpressShipped", "express", "all ExpressShipped", "completed"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Struct_Notifications_Reach_Exact_And_Interface_Typed_Handlers_On_Every_Path(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        await Assert.That(await RunAsync(host, "struct typed", cancellationToken))
                    .IsEquivalentTo(["journal Tick tick 1", "tick 1", "all Tick", "completed"], CollectionOrdering.Matching);
        await Assert.That(await RunAsync(host, "struct generic", cancellationToken))
                    .IsEquivalentTo(["journal Tick tick 2", "tick 2", "all Tick", "completed"], CollectionOrdering.Matching);
        await Assert.That(await RunAsync(host, "struct boxed by caller", cancellationToken))
                    .IsEquivalentTo(["journal Tick tick 3", "tick 3", "all Tick", "completed"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Notification_Type_The_Host_Does_Not_Know_Runs_No_Handlers(CancellationToken cancellationToken) =>
        // Not even the INotification catch-all: polymorphic handlers receive only the types the host knows.
        await Assert.That(await RunAsync("unknown", cancellationToken, new Foreign())).IsEquivalentTo(["completed"], CollectionOrdering.Matching);

    [Test]
    public async Task Every_Handler_Runs_And_Every_Failure_Is_Aggregated_In_Handler_Order(CancellationToken cancellationToken) =>
        await Assert.That(await RunAsync("failing", cancellationToken))
                    .IsEquivalentTo(
                         [
                             "3 ran", "all Failing",
                             "aggregate: InvalidOperationException sync 1 | InvalidOperationException async 2 | InvalidOperationException faulted 4",
                         ],
                         CollectionOrdering.Matching
                     );

    [Test]
    public async Task One_Failure_Is_Still_An_AggregateException(CancellationToken cancellationToken) =>
        await Assert.That(await RunAsync("one failure", cancellationToken))
                    .IsEquivalentTo(["all OneFailure", "aggregate: InvalidOperationException only"], CollectionOrdering.Matching);

    [Test]
    public async Task Cancellation_Skips_No_Handler_And_Is_Collected_Like_Any_Failure(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        await Assert.That(await RunAsync(host, "cancellable", cancellationToken))
                    .IsEquivalentTo(
                         ["2 saw cancelled True", "all Cancellable", "aggregate: OperationCanceledException The operation was canceled. | TaskCanceledException A task was canceled."],
                         CollectionOrdering.Matching
                     );
        await Assert.That(await RunAsync(host, "cancellable parallel", cancellationToken))
                    .IsEquivalentTo(
                         [
                             "2 saw cancelled True", "all CancellableParallel",
                             "aggregate: OperationCanceledException The operation was canceled. | TaskCanceledException A task was canceled.",
                         ],
                         CollectionOrdering.Matching
                     );
    }

    [Test]
    public async Task Parallel_Publish_Starts_Every_Handler_Before_Awaiting_Any(CancellationToken cancellationToken) =>
        // Sequentially, the first handler would wait forever for the second.
        await Assert.That(await RunAsync("fanout", cancellationToken))
                    .IsEquivalentTo(["1 start", "2 start", "1 end", "2 end", "all Fanout", "completed"], CollectionOrdering.Matching);

    [Test]
    public async Task Parallel_Publish_Aggregates_Synchronous_And_Asynchronous_Failures_In_Handler_Order(CancellationToken cancellationToken) =>
        await Assert.That(await RunAsync("fanout failing", cancellationToken))
                    .IsEquivalentTo(
                         [
                             "3 started", "all FanoutFailing",
                             "aggregate: InvalidOperationException sync 1 | InvalidOperationException async 2 | InvalidOperationException faulted 4",
                         ],
                         CollectionOrdering.Matching
                     );

    [Test]
    public async Task A_Notification_Behavior_Exception_Propagates_Unwrapped(CancellationToken cancellationToken)
    {
        // OQ-43: a synchronous throw from the outermost notification behavior throws from Publish itself, on the typed and generic paths.
        var host = await CompileHostAsync(Source, cancellationToken);
        await Assert.That(await RunAsync(host, "publish throws", cancellationToken))
                    .IsEquivalentTo(["threw synchronously publish sync", "completed"], CollectionOrdering.Matching);
        await Assert.That(await RunAsync(host, "publish throws generic", cancellationToken))
                    .IsEquivalentTo(["threw synchronously publish sync", "completed"], CollectionOrdering.Matching);

        // Thrown after next completed: not wrapped in an AggregateException.
        await Assert.That(await RunAsync(host, "publish throws later", cancellationToken))
                    .IsEquivalentTo(["handler ran", "all PublishThrowsLater", "threw InvalidOperationException publish async"], CollectionOrdering.Matching);

        // Let through from next: the strategy's one AggregateException, not wrapped again.
        await Assert.That(await RunAsync(host, "publish passes failure", cancellationToken))
                    .IsEquivalentTo(["pass>", "all PublishPassesFailure", "aggregate: InvalidOperationException handler failed"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Derived_Type_The_Host_Does_Not_Know_Runs_Nothing_Through_The_Base_Overload(CancellationToken cancellationToken) =>
        // OQ-43: the typed overload of the non-sealed Shipped redispatches on the runtime type, which the host does not know.
        await Assert.That(await RunAsync("derived unknown", cancellationToken)).IsEquivalentTo(["completed"], CollectionOrdering.Matching);

    [Test]
    public async Task A_Null_Notification_Throws_Synchronously(CancellationToken cancellationToken) =>
        await Assert.That(await RunAsync("null", cancellationToken))
                    .IsEquivalentTo(["threw synchronously", "threw synchronously", "threw synchronously", "completed"], CollectionOrdering.Matching);

    [Test]
    public async Task Notification_Handlers_Are_Registered_And_Resolved_Once_Per_Scope(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        await using var provider = BuildProvider(host, Registrations(host));
        var handlerType = host.GetType("Sample.AuditDomainEvents", throwOnError: true)!;

        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        await Assert.That(first.ServiceProvider.GetRequiredService(handlerType)).IsSameReferenceAs(first.ServiceProvider.GetRequiredService(handlerType));
        await Assert.That(first.ServiceProvider.GetRequiredService(handlerType)).IsNotSameReferenceAs(second.ServiceProvider.GetRequiredService(handlerType));
    }

    [Test]
    public async Task FrozenDictionary_Dispatch_Publishes_Each_Notification_To_Its_Own_Handlers(CancellationToken cancellationToken)
    {
        // Above the threshold generic Publish looks the index up and switches on it; every arm reinterprets the notification with
        // Unsafe.As, and all N{n} share one layout, so a mismatched index or arm would quietly run another type's handlers.
        var count = Analyzers.Hosts.HostEmitter.DispatchThreshold + 1;
        var host = await CompileHostAsync(ManyNotifications(count), cancellationToken);
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService(host.GetType("Sample.KeryxMediator", throwOnError: true)!);
        await Assert.That(mediator.GetType().GetField("__notificationTypes", BindingFlags.NonPublic | BindingFlags.Static)).IsNotNull();

        for (var i = 0; i < count; i++)
        {
            object value = 100 + i;
            var notification = (INotification)Create(host, string.Create(CultureInfo.InvariantCulture, $"Sample.N{i:00}"), value);
            await Assert.That(await Call<Task<string[]>>(host, "Sample.Probe", "Generic", scope.ServiceProvider, notification, cancellationToken))
                        .IsEquivalentTo([string.Create(CultureInfo.InvariantCulture, $"{i:00} {100 + i}")])
                        .Because(string.Create(CultureInfo.InvariantCulture, $"N{i:00} through IPublisher"));
        }

        var last = count - 1;
        await Assert.That(await Call<Task<string[]>>(host, "Sample.Probe", "TypedLast", scope.ServiceProvider, 7, cancellationToken))
                    .IsEquivalentTo([string.Create(CultureInfo.InvariantCulture, $"{last:00} 7")]);

        // An unknown type takes the default arm and runs nothing.
        await Assert.That(await Call<Task<string[]>>(host, "Sample.Probe", "Generic", scope.ServiceProvider, new Foreign(), cancellationToken)).IsEmpty();
    }

    [Test]
    [Arguments("typed")]
    [Arguments("generic")]
    [Arguments("generic as interface")]
    public async Task A_Referenced_Notification_Type_Reaches_The_Hosts_Polymorphic_Handlers(string scenario, CancellationToken cancellationToken)
    {
        // OQ-42 (b): the host knows Contracts.OrderShipped only because one of its handlers takes it, and then delivers it to every handler
        // it converts to, in handler order.
        var contracts = CompileLibrary(
            "Contracts",
            """
            namespace Contracts;

            public interface IDomainEvent : global::Keryx.INotification { string Id { get; } }
            public sealed record OrderShipped(string Id) : IDomainEvent;
            """
        );
        var host = await CompileHostAsync(ReferencedNotificationHost, "Contracts", contracts, cancellationToken);
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        await Assert.That(await Call<Task<string[]>>(host, "Sample.Probe", "Run", scope.ServiceProvider, scenario, cancellationToken))
                    .IsEquivalentTo(["exact 1", "domain 1", "all OrderShipped"], CollectionOrdering.Matching);
    }

    private const string LogSource = """
        public static class Log
        {
            private static readonly List<string> Entries = [];

            public static void Write(string entry)
            {
                lock (Entries)
                {
                    Entries.Add(entry);
                }
            }

            public static string[] Take()
            {
                lock (Entries)
                {
                    var entries = Entries.ToArray();
                    Entries.Clear();
                    return entries;
                }
            }
        }

        """;

    private const string ReferencedNotificationHost = """
        namespace Sample;

        """ + LogSource + """
        public static partial class OnOrderShipped
        {
            private static ValueTask Handle(Contracts.OrderShipped notification)
            {
                Log.Write("exact " + notification.Id);
                return default;
            }
        }

        public static partial class OnDomainEvent
        {
            [HandlerOrder(1)]
            private static ValueTask Handle(Contracts.IDomainEvent notification)
            {
                Log.Write("domain " + notification.Id);
                return default;
            }
        }

        public static partial class CatchAll
        {
            [HandlerOrder(5)]
            private static ValueTask Handle(INotification notification)
            {
                Log.Write("all " + notification.GetType().Name);
                return default;
            }
        }

        public static class Probe
        {
            public static async Task<string[]> Run(IServiceProvider services, string scenario, CancellationToken cancellationToken)
            {
                var mediator = services.GetRequiredService<KeryxMediator>();
                IPublisher publisher = mediator;
                _ = Log.Take();
                var notification = new Contracts.OrderShipped("1");
                switch (scenario)
                {
                    case "typed":
                        await mediator.Publish(notification, cancellationToken);
                        break;
                    case "generic":
                        await publisher.Publish(notification, cancellationToken);
                        break;
                    case "generic as interface":
                        await publisher.Publish<INotification>(notification, cancellationToken);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
                }

                return Log.Take();
            }
        }
        """;

    /// <summary>A host with <paramref name="count" /> notification types <c>N00</c>... of one layout, each with a handler that logs its index and value.</summary>
    private static string ManyNotifications(int count)
    {
        var types = string.Concat(
            Enumerable.Range(0, count)
                      .Select(static i => string.Create(
                                  CultureInfo.InvariantCulture,
                                  $$"""
                                  public sealed record N{{i:00}}(int Value) : INotification;
                                  public static partial class N{{i:00}}Handler
                                  {
                                      private static ValueTask Handle(N{{i:00}} notification)
                                      {
                                          Log.Write("{{i:00}} " + notification.Value);
                                          return default;
                                      }
                                  }

                                  """
                              )
                       )
        );
        var last = string.Create(CultureInfo.InvariantCulture, $"N{count - 1:00}");
        return "namespace Sample;\n\n"
             + LogSource
             + types
             + $$"""
                 public static class Probe
                 {
                     public static async Task<string[]> Generic(IServiceProvider services, INotification notification, CancellationToken cancellationToken)
                     {
                         _ = Log.Take();
                         await services.GetRequiredService<IPublisher>().Publish(notification, cancellationToken);
                         return Log.Take();
                     }

                     public static async Task<string[]> TypedLast(IServiceProvider services, int value, CancellationToken cancellationToken)
                     {
                         _ = Log.Take();
                         await services.GetRequiredService<KeryxMediator>().Publish(new {{last}}(value), cancellationToken);
                         return Log.Take();
                     }
                 }
                 """;
    }

    private async Task<string[]> RunAsync(string scenario, CancellationToken cancellationToken, object? argument = null) =>
        await RunAsync(await CompileHostAsync(Source, cancellationToken), scenario, cancellationToken, argument);

    /// <summary>Runs one scenario in a new scope of a new provider over an already compiled host (a host is loaded once per test).</summary>
    private static async Task<string[]> RunAsync(Assembly host, string scenario, CancellationToken cancellationToken, object? argument = null)
    {
        await using var provider = BuildProvider(host, Registrations(host));
        await using var scope = provider.CreateAsyncScope();
        return await Call<Task<string[]>>(host, "Sample.Probe", "Run", scope.ServiceProvider, scenario, argument, cancellationToken);
    }

    /// <summary>Registers the host's <c>Journal</c> as its <c>IJournal</c>, which <c>AuditDomainEvents</c> injects.</summary>
    private static Action<IServiceCollection> Registrations(Assembly host) =>
        services => services.AddSingleton(host.GetType("Sample.IJournal", throwOnError: true)!, Create(host, "Sample.Journal"));

    /// <summary>A notification type declared outside the host, so the host does not know it.</summary>
    public sealed class Foreign : INotification;
}
