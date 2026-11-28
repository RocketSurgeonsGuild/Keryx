using System.Reflection;
using Keryx.Tests.Hosts;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions.Enums;

namespace Keryx.Tests.Notifications;

/// <summary>
///     Compiles a host with notification behaviors and publishes through it (ADR 0001, Notifications, Behaviors): static
///     <c>[NotificationBehavior]</c>s from all three scopes (on the type, per assembly, global on the class or only on the method), sorted by
///     phase, wrapping the whole publish once; <c>TNotification</c> always the concrete published type; constraints checked against it;
///     <c>ExcludeBehavior</c>; notification behaviors with zero handlers (OQ-18); a behavior that does not call <c>next</c>; handlers called
///     directly, with no behaviors of their own; and <c>[assembly: Behavior]</c> applying only in the notification type's assembly.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class NotificationBehaviorTests : HostTest
{
    internal const string Source = """
        [assembly: Behavior(typeof(Sample.AssemblyPublish))]
        [assembly: ExcludeBehavior(typeof(Sample.ExcludedGlobal))]

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

        public interface IGreeter { string Greet(string name); }
        public sealed class Greeter : IGreeter { public string Greet(string name) => "global-publish " + name; }

        public interface IAudited : INotification { }
        public interface IReviewed : INotification { }

        [NotificationBehavior]
        [BehaviorOrder(Phase.Outermost)]
        public static class TypePublish
        {
            public static async ValueTask Handle<T, TNext>(T notification, TNext next, CancellationToken cancellationToken)
                where T : INotification
                where TNext : struct, INext
            {
                Log.Write("type-publish>");
                await next.InvokeAsync(cancellationToken);
                Log.Write("<type-publish");
            }
        }

        [NotificationBehavior]
        [BehaviorOrder(Phase.Observability)]
        public static class AssemblyPublish
        {
            public static ValueTask Handle<T, TNext>(T notification, TNext next, CancellationToken cancellationToken)
                where T : IAudited
                where TNext : struct, INext
            {
                Log.Write("assembly-publish " + typeof(T).Name);
                return next.InvokeAsync(cancellationToken);
            }
        }

        [NotificationBehavior]
        [GlobalBehavior]
        [BehaviorOrder(Phase.Security)]
        public static class GlobalPublish
        {
            public static ValueTask Handle<T, TNext>(T notification, IGreeter greeter, TNext next, CancellationToken cancellationToken)
                where T : IAudited
                where TNext : struct, INext
            {
                Log.Write(greeter.Greet(typeof(T).Name));
                return next.InvokeAsync(cancellationToken);
            }
        }

        /// <summary>Global only through its notification behavior method; its request behavior is not global.</summary>
        [NotificationBehavior]
        [RequestBehavior]
        [BehaviorOrder(Phase.Validation)]
        public static class MethodGlobal
        {
            [GlobalBehavior]
            public static ValueTask Handle<T, TNext>(T notification, TNext next, CancellationToken cancellationToken)
                where T : IReviewed
                where TNext : struct, INext
            {
                Log.Write("method-global " + typeof(T).Name);
                return next.InvokeAsync(cancellationToken);
            }

            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse>
            {
                Log.Write("never");
                return next.InvokeAsync(cancellationToken);
            }
        }

        [NotificationBehavior]
        public static class ClosedPublish
        {
            public static ValueTask Handle<TNext>(Audited notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext
            {
                Log.Write("closed-publish " + notification.Name);
                return next.InvokeAsync(cancellationToken);
            }
        }

        [NotificationBehavior]
        [GlobalBehavior]
        public static class ExcludedGlobal
        {
            public static ValueTask Handle<T, TNext>(T notification, TNext next) where T : INotification where TNext : struct, INext
            {
                Log.Write("excluded");
                return next.InvokeAsync(default);
            }
        }

        /// <summary>Never calls the rest of the publish pipeline.</summary>
        [NotificationBehavior]
        public static class Skip
        {
            public static ValueTask Handle<TNext>(Plain notification, TNext next) where TNext : struct, INext
            {
                Log.Write("skip");
                return default;
            }
        }

        [Behavior(typeof(TypePublish))]
        [Behavior(typeof(ClosedPublish))]
        public sealed record Audited(string Name) : IAudited, IReviewed;
        public sealed record Reviewed2 : IReviewed;
        public sealed record Quiet : IAudited;
        public sealed record Silent : INotification;
        /// <summary>No handlers, and its only behavior injects nothing: a publish pipeline without a context, whose last link takes nothing.</summary>
        [Behavior(typeof(TypePublish))]
        public sealed record Unattended : INotification;
        [Behavior(typeof(Skip))]
        public sealed record Plain : INotification;
        public readonly record struct Tock(int Value) : IReviewed;
        public sealed record Ping : IRequest<string>;

        [PublishStrategy(PublishStrategy.Parallel)]
        [Behavior(typeof(TypePublish))]
        public sealed record Broadcast : INotification;

        /// <summary>Handler 1 waits for handler 2, so it finishes only when both have started.</summary>
        public static class Gate
        {
            public static TaskCompletionSource Open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static partial class OnBroadcast1
        {
            [HandlerOrder(1)]
            private static async ValueTask Handle(Broadcast notification)
            {
                Log.Write("broadcast 1>");
                await Gate.Open.Task;
                Log.Write("<broadcast 1");
            }
        }

        public static partial class OnBroadcast2
        {
            [HandlerOrder(2)]
            private static ValueTask Handle(Broadcast notification)
            {
                Log.Write("broadcast 2");
                Gate.Open.TrySetResult();
                return default;
            }
        }

        public static partial class OnAudited
        {
            private static ValueTask Handle(Audited notification)
            {
                Log.Write("handler " + notification.Name);
                return default;
            }
        }

        public static partial class OnReviewed
        {
            private static ValueTask Handle(IReviewed notification)
            {
                Log.Write("reviewed " + notification.GetType().Name + (notification is Tock tock ? " " + tock.Value : ""));
                return default;
            }
        }

        public static partial class OnPlain
        {
            private static ValueTask Handle(Plain notification) => throw new InvalidOperationException("Skip does not call next.");
        }

        public static partial class PingHandler
        {
            private static string Handle(Ping request)
            {
                Log.Write("ping");
                return "pong";
            }
        }

        public static class Probe
        {
            public static async Task<string[]> Run(IServiceProvider services, string scenario)
            {
                var mediator = services.GetRequiredService<KeryxMediator>();
                IPublisher publisher = mediator;
                _ = Log.Take();
                switch (scenario)
                {
                    case "audited typed":
                        await mediator.Publish(new Audited("a"));
                        break;
                    case "audited generic":
                        await publisher.Publish<IAudited>(new Audited("a"));
                        break;
                    case "reviewed":
                        await publisher.Publish(new Reviewed2());
                        break;
                    case "quiet":
                        await mediator.Publish(new Quiet());
                        break;
                    case "unattended typed":
                        await mediator.Publish(new Unattended());
                        break;
                    case "unattended generic":
                        await publisher.Publish<Unattended>(new Unattended());
                        Log.Write("completed");
                        break;
                    case "silent":
                        Log.Write("completed synchronously " + mediator.Publish(new Silent()).IsCompletedSuccessfully);
                        break;
                    case "plain":
                        await publisher.Publish(new Plain());
                        break;
                    case "tock":
                        await publisher.Publish(new Tock(7));
                        break;
                    case "broadcast":
                        Gate.Open = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        await publisher.Publish(new Broadcast());
                        break;
                    case "ping":
                        Log.Write(await mediator.Send(new Ping()));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
                }

                return Log.Take();
            }
        }
        """;

    [Test]
    public async Task Notification_Behaviors_From_Every_Scope_Wrap_The_Whole_Publish_In_Phase_Order(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        string[] expected =
        [
            // On the type (TypePublish, Outermost; ClosedPublish, Default), the assembly's (Observability) and the globals (Security; the
            // method-only one, Validation). ExcludedGlobal is excluded.
            "type-publish>", "assembly-publish Audited", "global-publish Audited", "method-global Audited", "closed-publish a",
            // Then the handlers, called directly: the exact-type one and the polymorphic one.
            "handler a", "reviewed Audited",
            "<type-publish",
        ];

        await Assert.That(await RunAsync(host, "audited typed")).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(await RunAsync(host, "audited generic")).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Constraints_Are_Checked_Against_The_Concrete_Published_Type(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);

        // Reviewed2 is not IAudited: only the IReviewed global fits.
        await Assert.That(await RunAsync(host, "reviewed")).IsEquivalentTo(["method-global Reviewed2", "reviewed Reviewed2"], CollectionOrdering.Matching);

        // A struct: the behavior sees the struct, the interface-typed handler the one box.
        await Assert.That(await RunAsync(host, "tock")).IsEquivalentTo(["method-global Tock", "reviewed Tock 7"], CollectionOrdering.Matching);

        // The method-only global is a notification behavior only: the request pipeline does not get it.
        await Assert.That(await RunAsync(host, "ping")).IsEquivalentTo(["ping", "pong"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Notification_Behaviors_Run_With_Zero_Handlers(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);

        await Assert.That(await RunAsync(host, "quiet")).IsEquivalentTo(["assembly-publish Quiet", "global-publish Quiet"], CollectionOrdering.Matching);
        // No handlers and a behavior that injects nothing: the last link runs nothing and the publish completes.
        await Assert.That(await RunAsync(host, "unattended typed")).IsEquivalentTo(["type-publish>", "<type-publish"], CollectionOrdering.Matching);
        await Assert.That(await RunAsync(host, "unattended generic")).IsEquivalentTo(["type-publish>", "<type-publish", "completed"], CollectionOrdering.Matching);
        // Neither handlers nor notification behaviors: Publish completes immediately.
        await Assert.That(await RunAsync(host, "silent")).IsEquivalentTo(["completed synchronously True"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Notification_Behavior_Wraps_A_Parallel_Publish_Once(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);

        // Both handlers start before either finishes; the behavior completes after both.
        await Assert.That(await RunAsync(host, "broadcast"))
                    .IsEquivalentTo(["type-publish>", "broadcast 1>", "broadcast 2", "<broadcast 1", "<type-publish"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Notification_Behavior_Can_Skip_The_Handlers(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);

        await Assert.That(await RunAsync(host, "plain")).IsEquivalentTo(["skip"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task AddKeryx_Registers_No_Notification_Behaviors(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        var services = new ServiceCollection();
        AddKeryx(host, services);

        await Assert.That(services.Select(static d => d.ServiceType.Name).Order(StringComparer.Ordinal).ToArray())
                    .IsEquivalentTo(["IMediator", "IPublisher", "ISender", "KeryxMediator", "__KeryxScopeState"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task An_Assembly_Behavior_Applies_Only_In_The_Notification_Types_Assembly(CancellationToken cancellationToken)
    {
        // Contracts declares Placed and attaches ContractsLog to its own notification types; Handlers declares a handler of Placed and
        // attaches HandlersLog to "its" notification types, of which it declares none, so HandlersLog changes no publish pipeline.
        const string contracts = """
            using System.Threading.Tasks;
            using Keryx;

            [assembly: Behavior(typeof(Contracts.ContractsLog))]

            namespace Contracts;

            public sealed record Placed : INotification;

            public static class Trail
            {
                public static readonly System.Collections.Generic.List<string> Entries = [];
            }

            [NotificationBehavior]
            public static class ContractsLog
            {
                public static ValueTask Handle<T, TNext>(T notification, TNext next) where T : INotification where TNext : struct, INext
                {
                    Trail.Entries.Add("contracts-log " + typeof(T).Name);
                    return next.InvokeAsync(default);
                }
            }
            """;
        const string handlers = """
            using System.Threading.Tasks;
            using Keryx;

            [assembly: Behavior(typeof(Handlers.HandlersLog))]

            namespace Handlers;

            [NotificationBehavior]
            public static class HandlersLog
            {
                public static ValueTask Handle<T, TNext>(T notification, TNext next) where T : INotification where TNext : struct, INext
                {
                    Contracts.Trail.Entries.Add("handlers-log " + typeof(T).Name);
                    return next.InvokeAsync(default);
                }
            }

            public static partial class OnPlaced
            {
                private static ValueTask Handle(Contracts.Placed notification)
                {
                    Contracts.Trail.Entries.Add("handler");
                    return default;
                }
            }
            """;
        var contractsLibrary = await CompileLibraryAsync("Contracts", contracts, cancellationToken);
        var handlersLibrary = await CompileLibraryAsync("Handlers", handlers, cancellationToken, references: [contractsLibrary]);
        var host = await CompileHostAsync(
            """
            namespace Sample;

            public static class Probe
            {
                public static async Task<string[]> Run(IServiceProvider services)
                {
                    await services.GetRequiredService<KeryxMediator>().Publish(new Contracts.Placed());
                    return [.. Contracts.Trail.Entries];
                }
            }
            """,
            cancellationToken,
            contractsLibrary,
            handlersLibrary
        );

        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        await Assert.That(await Call<Task<string[]>>(host, "Sample.Probe", "Run", scope.ServiceProvider))
                    .IsEquivalentTo(["contracts-log Placed", "handler"], CollectionOrdering.Matching);
    }

    private static async Task<string[]> RunAsync(Assembly host, string scenario)
    {
        await using var provider = BuildProvider(
            host,
            services => services.AddSingleton(host.GetType("Sample.IGreeter", throwOnError: true)!, Create(host, "Sample.Greeter"))
        );
        await using var scope = provider.CreateAsyncScope();
        return await Call<Task<string[]>>(host, "Sample.Probe", "Run", scope.ServiceProvider, scenario);
    }
}
