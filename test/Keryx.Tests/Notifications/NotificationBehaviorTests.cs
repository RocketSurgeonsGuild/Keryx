using System.Reflection;
using Keryx.Tests.Hosts;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions.Enums;

namespace Keryx.Tests.Notifications;

/// <summary>
///     Compiles a host with notification behaviors and publishes through it (ADR 0001, Notifications, Behaviors): per-publish behaviors
///     (<c>INotificationPipelineBehavior</c>) and per-handler behaviors (<c>INotificationHandlerPipelineBehavior</c>) from all three scopes,
///     sorted by phase; <c>TNotification</c> always the concrete published type, also for a polymorphic handler; constraints checked against
///     each concrete type (OQ-40); <c>ExcludeBehavior</c>; per-publish behaviors with zero handlers (OQ-18); a behavior that does not call
///     <c>next</c>; a struct notification whose interface-typed handler has behaviors.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class NotificationBehaviorTests : HostTest
{
    internal const string Source = """
        [assembly: KeryxHost]
        [assembly: Behavior(typeof(Sample.AssemblyPublish<>))]
        [assembly: Behavior(typeof(Sample.AssemblyHandler<>))]
        [assembly: ExcludeBehavior(typeof(Sample.ExcludedGlobal<>))]

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
        public sealed class Greeter : IGreeter { public string Greet(string name) => "global-handler " + name; }

        public interface IAudited : INotification { }
        public interface IReviewed : INotification { }

        [BehaviorOrder(Phase.Outermost)]
        public sealed class TypePublish<T> : INotificationPipelineBehavior<T> where T : INotification
        {
            public async ValueTask Handle<TNext>(T notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext
            {
                Log.Write("type-publish>");
                await next.InvokeAsync(cancellationToken);
                Log.Write("<type-publish");
            }
        }

        [BehaviorOrder(Phase.Observability)]
        public sealed class AssemblyPublish<T> : INotificationPipelineBehavior<T> where T : IAudited
        {
            public ValueTask Handle<TNext>(T notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext
            {
                Log.Write("assembly-publish " + typeof(T).Name);
                return next.InvokeAsync(cancellationToken);
            }
        }

        [GlobalBehavior]
        [BehaviorOrder(Phase.Security)]
        public sealed class GlobalPublish<T> : INotificationPipelineBehavior<T> where T : IAudited
        {
            public ValueTask Handle<TNext>(T notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext
            {
                Log.Write("global-publish " + typeof(T).Name);
                return next.InvokeAsync(cancellationToken);
            }
        }

        public sealed class ClosedPublish : INotificationPipelineBehavior<Audited>
        {
            public ValueTask Handle<TNext>(Audited notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext
            {
                Log.Write("closed-publish " + notification.Name);
                return next.InvokeAsync(cancellationToken);
            }
        }

        [GlobalBehavior]
        public sealed class ExcludedGlobal<T> : INotificationHandlerPipelineBehavior<T> where T : INotification
        {
            public ValueTask Handle<TNext>(T notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext
            {
                Log.Write("excluded");
                return next.InvokeAsync(cancellationToken);
            }
        }

        [BehaviorOrder(Phase.Outermost)]
        public sealed class MethodHandler<T> : INotificationHandlerPipelineBehavior<T> where T : INotification
        {
            public async ValueTask Handle<TNext>(T notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext
            {
                Log.Write("method-handler " + typeof(T).Name + ">");
                await next.InvokeAsync(cancellationToken);
                Log.Write("<method-handler " + typeof(T).Name);
            }
        }

        [GlobalBehavior]
        [BehaviorOrder(Phase.Security)]
        public sealed class GlobalHandler<T>(IGreeter greeter) : INotificationHandlerPipelineBehavior<T> where T : INotification
        {
            public ValueTask Handle<TNext>(T notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext
            {
                Log.Write(greeter.Greet(typeof(T).Name));
                return next.InvokeAsync(cancellationToken);
            }
        }

        [BehaviorOrder(Phase.Validation)]
        public sealed class AssemblyHandler<T> : INotificationHandlerPipelineBehavior<T> where T : IAudited
        {
            public ValueTask Handle<TNext>(T notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext
            {
                Log.Write("assembly-handler " + typeof(T).Name);
                return next.InvokeAsync(cancellationToken);
            }
        }

        /// <summary>Attached to a polymorphic handler: applies only for the concrete types that are IAudited (OQ-40).</summary>
        public sealed class OnlyAudited<T> : INotificationHandlerPipelineBehavior<T> where T : IAudited
        {
            public ValueTask Handle<TNext>(T notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext
            {
                Log.Write("only-audited " + typeof(T).Name);
                return next.InvokeAsync(cancellationToken);
            }
        }

        /// <summary>Never calls the rest of the handler's pipeline.</summary>
        public sealed class Skip<T> : INotificationHandlerPipelineBehavior<T> where T : INotification
        {
            public ValueTask Handle<TNext>(T notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext
            {
                Log.Write("skip");
                return default;
            }
        }

        [Behavior(typeof(TypePublish<>))]
        [Behavior(typeof(ClosedPublish))]
        public sealed record Audited(string Name) : IAudited, IReviewed;
        public sealed record Reviewed2 : IReviewed;
        public sealed record Quiet : IAudited;
        public sealed record Silent : INotification;
        public sealed record Plain : INotification;
        public readonly record struct Tock(int Value) : IReviewed;

        public static partial class OnAudited
        {
            [Behavior(typeof(MethodHandler<>))]
            private static ValueTask Handle(Audited notification)
            {
                Log.Write("handler " + notification.Name);
                return default;
            }
        }

        public static partial class OnReviewed
        {
            [Behavior(typeof(MethodHandler<>))]
            [Behavior(typeof(OnlyAudited<>))]
            private static ValueTask Handle(IReviewed notification)
            {
                Log.Write("reviewed " + notification.GetType().Name + (notification is Tock tock ? " " + tock.Value : ""));
                return default;
            }
        }

        public static partial class OnPlain
        {
            [Behavior(typeof(Skip<>))]
            private static ValueTask Handle(Plain notification) => throw new InvalidOperationException("Skip does not call next.");
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
                    case "silent":
                        Log.Write("completed synchronously " + mediator.Publish(new Silent()).IsCompletedSuccessfully);
                        break;
                    case "plain":
                        await publisher.Publish(new Plain());
                        break;
                    case "tock":
                        await publisher.Publish(new Tock(7));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
                }

                return Log.Take();
            }
        }
        """;

    [Test]
    public async Task Per_Publish_And_Per_Handler_Behaviors_From_Every_Scope_Run_In_Phase_Order(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        string[] expected =
        [
            // Per publish: on the type (TypePublish, Outermost; ClosedPublish, Default), the assembly's and the global one (both IAudited).
            "type-publish>", "assembly-publish Audited", "global-publish Audited", "closed-publish a",
            // OnAudited: MethodHandler (Outermost), GlobalHandler (Security), AssemblyHandler (Validation). ExcludedGlobal is excluded.
            "method-handler Audited>", "global-handler Audited", "assembly-handler Audited", "handler a", "<method-handler Audited",
            // OnReviewed (polymorphic), closed over the concrete Audited: OnlyAudited fits it.
            "method-handler Audited>", "global-handler Audited", "assembly-handler Audited", "only-audited Audited", "reviewed Audited",
            "<method-handler Audited",
            "<type-publish",
        ];

        await Assert.That(await RunAsync(host, "audited typed")).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(await RunAsync(host, "audited generic")).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Polymorphic_Handlers_Behaviors_Are_Closed_Over_Each_Concrete_Type(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);

        // Reviewed2 is not IAudited: no per-publish behavior fits it, and neither AssemblyHandler nor the attached OnlyAudited apply.
        await Assert.That(await RunAsync(host, "reviewed"))
                    .IsEquivalentTo(
                         ["method-handler Reviewed2>", "global-handler Reviewed2", "reviewed Reviewed2", "<method-handler Reviewed2"],
                         CollectionOrdering.Matching
                     );

        // A struct delivered to the interface-typed handler: the behaviors see the struct, the handler the one box.
        await Assert.That(await RunAsync(host, "tock"))
                    .IsEquivalentTo(["method-handler Tock>", "global-handler Tock", "reviewed Tock 7", "<method-handler Tock"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Per_Publish_Behaviors_Run_With_Zero_Handlers(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);

        await Assert.That(await RunAsync(host, "quiet"))
                    .IsEquivalentTo(["assembly-publish Quiet", "global-publish Quiet"], CollectionOrdering.Matching);
        // Neither handlers nor per-publish behaviors: Publish completes immediately.
        await Assert.That(await RunAsync(host, "silent")).IsEquivalentTo(["completed synchronously True"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Per_Handler_Behavior_Can_Skip_The_Handler(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);

        await Assert.That(await RunAsync(host, "plain")).IsEquivalentTo(["global-handler Plain", "skip"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task AddKeryx_Registers_Every_Closed_Notification_Behavior(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        var services = new ServiceCollection();
        AddKeryx(host, services);

        var registered = services
                        .Select(static d => d.ServiceType)
                        .Where(static t => t.IsGenericType)
                        .Select(static t => t.Name + "[" + string.Join(",", t.GenericTypeArguments.Select(static a => a.Name)) + "]")
                        .Order(StringComparer.Ordinal)
                        .ToArray();
        await Assert.That(registered)
                    .IsEquivalentTo(
                         [
                             "AssemblyHandler`1[Audited]",
                             "AssemblyPublish`1[Audited]",
                             "AssemblyPublish`1[Quiet]",
                             "GlobalHandler`1[Audited]",
                             "GlobalHandler`1[Plain]",
                             "GlobalHandler`1[Reviewed2]",
                             "GlobalHandler`1[Tock]",
                             "GlobalPublish`1[Audited]",
                             "GlobalPublish`1[Quiet]",
                             "MethodHandler`1[Audited]",
                             "MethodHandler`1[Reviewed2]",
                             "MethodHandler`1[Tock]",
                             "OnlyAudited`1[Audited]",
                             "Skip`1[Plain]",
                             "TypePublish`1[Audited]",
                         ],
                         CollectionOrdering.Matching
                     );
        await Assert.That(services.Any(static d => d.ServiceType.Name == "ClosedPublish")).IsTrue();
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
