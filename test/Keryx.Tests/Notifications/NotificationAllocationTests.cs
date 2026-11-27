using Keryx.Tests.Hosts;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Tests.Notifications;

/// <summary>
///     The Performance contract for <c>Publish</c> (ADR 0001, Performance contract; the Phase 6 brief): after one warm-up publish in the same
///     scope, a sequential publish and a parallel publish whose handlers complete synchronously allocate 0 bytes, on the typed
///     overload and the generic entry point, for class and struct notifications, with and without static notification behaviors (one that
///     takes an injected service, and an <c>async</c> one, compiled with optimizations as a Release build ships it), with and without a
///     pipeline context (a pipeline whose handlers are all static and whose behaviors inject nothing has none, OQ-70). The one documented
///     exception, a struct delivered to an interface-typed handler, allocates exactly one box per publish (OQ-17, KRX037).
/// </summary>
/// <remarks>Measured like <see cref="HostAllocationTests" />: inside the compiled host, smallest of several regions, not in parallel.</remarks>
[Timeout(ModuleInitializer.TestTimeout)]
[NotInParallel]
public class NotificationAllocationTests : HostTest
{
    private const string Source = """
        namespace Sample;

        public interface IMarker : INotification { }

        [NotificationBehavior]
        [BehaviorOrder(Phase.Outermost)]
        public static class Around
        {
            public static async ValueTask Handle<T, TNext>(T notification, TNext next, CancellationToken cancellationToken)
                where T : INotification
                where TNext : struct, INext =>
                await next.InvokeAsync(cancellationToken);
        }

        [NotificationBehavior]
        public static class Pass
        {
            public static ValueTask Handle<T, TNext>(T notification, TNext next, object service, CancellationToken cancellationToken)
                where T : INotification
                where TNext : struct, INext
            {
                GC.KeepAlive(service);
                return next.InvokeAsync(cancellationToken);
            }
        }

        public sealed record Placed : INotification;
        public readonly record struct Tick(int Value) : INotification;
        public readonly record struct Tock(int Value) : IMarker;

        [Behavior(typeof(Around))]
        [Behavior(typeof(Pass))]
        public sealed record Wrapped : INotification;

        [PublishStrategy(PublishStrategy.Parallel)]
        public sealed record Fanout : INotification;

        /// <summary>No handlers, and its behavior injects nothing: a publish pipeline without a context.</summary>
        [Behavior(typeof(Around))]
        public sealed record Unwatched : INotification;

        [PublishStrategy(PublishStrategy.Parallel)]
        [Behavior(typeof(Pass))]
        public readonly record struct FanoutTick(int Value) : INotification;

        /// <summary>Static handlers and a behavior that injects nothing: a publish pipeline without a context (OQ-70), like <c>Tick</c>.</summary>
        [Behavior(typeof(Around))]
        public sealed record Quiet : INotification;

        /// <summary>Parallel, static handlers, no behaviors: no context.</summary>
        [PublishStrategy(PublishStrategy.Parallel)]
        public sealed record Spread : INotification;

        /// <summary>Parallel, static handlers, a behavior that injects nothing: no context.</summary>
        [PublishStrategy(PublishStrategy.Parallel)]
        [Behavior(typeof(Around))]
        public sealed record SpreadWrapped : INotification;

        public static partial class OnPlaced1 { private static ValueTask Handle(Placed notification) => default; }
        public static partial class OnPlaced2 { private static Task Handle(Placed notification, CancellationToken cancellationToken) => Task.CompletedTask; }
        public partial class OnPlaced3 { private static ValueTask Handle(Placed notification, object service) => default; }

        public static partial class OnTick1 { private static ValueTask Handle(Tick notification) => default; }
        public static partial class OnTick2 { private static ValueTask Handle(Tick notification, CancellationToken cancellationToken) => default; }

        public static partial class OnTock { private static ValueTask Handle(Tock notification) => default; }
        public static partial class OnAnyMarker { private static ValueTask Handle(IMarker notification) => default; }
        public static partial class OnAnyMarker2 { private static ValueTask Handle(IMarker notification) => default; }

        public static partial class OnWrapped1 { private static ValueTask Handle(Wrapped notification) => default; }
        public partial class OnWrapped2 { private static ValueTask Handle(Wrapped notification, object service) => default; }

        public static partial class OnFanout1 { private static ValueTask Handle(Fanout notification) => default; }
        public partial class OnFanout2 { private static ValueTask Handle(Fanout notification, object service) => default; }

        public static partial class OnFanoutTick1 { private static ValueTask Handle(FanoutTick notification) => default; }
        public static partial class OnFanoutTick2 { private static ValueTask Handle(FanoutTick notification) => default; }

        public static partial class OnQuiet1 { private static ValueTask Handle(Quiet notification) => default; }
        public static partial class OnQuiet2 { private static Task Handle(Quiet notification, CancellationToken cancellationToken) => Task.CompletedTask; }

        public static partial class OnSpread1 { private static ValueTask Handle(Spread notification) => default; }
        public static partial class OnSpread2 { private static ValueTask Handle(Spread notification) => default; }

        public static partial class OnSpreadWrapped1 { private static ValueTask Handle(SpreadWrapped notification) => default; }
        public static partial class OnSpreadWrapped2 { private static ValueTask Handle(SpreadWrapped notification) => default; }

        public static class Probe
        {
            private static object? _sink;

            public static Dictionary<string, long> Measure(IServiceProvider services)
            {
                var mediator = services.GetRequiredService<KeryxMediator>();
                IPublisher publisher = mediator;
                var placed = new Placed();
                var tick = new Tick(1);
                var tock = new Tock(1);
                var wrapped = new Wrapped();
                var fanout = new Fanout();
                var fanoutTick = new FanoutTick(1);
                var unwatched = new Unwatched();
                var quiet = new Quiet();
                var spread = new Spread();
                var spreadWrapped = new SpreadWrapped();

                return new()
                {
                    ["sequential, typed class"] = Region(() => mediator.Publish(placed)),
                    ["sequential, generic class"] = Region(() => publisher.Publish(placed)),
                    ["sequential, typed struct"] = Region(() => mediator.Publish(tick)),
                    ["sequential, generic struct"] = Region(() => publisher.Publish(tick)),
                    ["sequential, typed, behaviors"] = Region(() => mediator.Publish(wrapped)),
                    ["sequential, generic, behaviors"] = Region(() => publisher.Publish(wrapped)),
                    ["parallel, typed class"] = Region(() => mediator.Publish(fanout)),
                    ["parallel, generic class"] = Region(() => publisher.Publish(fanout)),
                    ["parallel, typed struct, behavior"] = Region(() => mediator.Publish(fanoutTick)),
                    ["parallel, generic struct, behavior"] = Region(() => publisher.Publish(fanoutTick)),
                    ["no handlers, typed, behavior without a context"] = Region(() => mediator.Publish(unwatched)),
                    ["no handlers, generic, behavior without a context"] = Region(() => publisher.Publish(unwatched)),
                    ["sequential, typed, behavior without a context"] = Region(() => mediator.Publish(quiet)),
                    ["sequential, generic, behavior without a context"] = Region(() => publisher.Publish(quiet)),
                    ["parallel, typed, without a context"] = Region(() => mediator.Publish(spread)),
                    ["parallel, generic, without a context"] = Region(() => publisher.Publish(spread)),
                    ["parallel, typed, behavior without a context"] = Region(() => mediator.Publish(spreadWrapped)),
                    ["parallel, generic, behavior without a context"] = Region(() => publisher.Publish(spreadWrapped)),
                    ["boxed, typed struct"] = Region(() => mediator.Publish(tock)),
                    ["boxed, generic struct"] = Region(() => publisher.Publish(tock)),
                    ["one box"] = Region(() =>
                        {
                            _sink = tock;
                            return default;
                        }
                    ),
                };
            }

            private static long Region(Func<ValueTask> publish)
            {
                Complete(publish());
                var smallest = long.MaxValue;
                for (var repeat = 0; repeat < 5; repeat++)
                {
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    for (var i = 0; i < 1_000; i++)
                    {
                        Complete(publish());
                    }

                    smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
                }

                return smallest;
            }

            private static void Complete(ValueTask task)
            {
                if (!task.IsCompletedSuccessfully)
                {
                    throw new InvalidOperationException("The publish did not complete synchronously.");
                }

                task.GetAwaiter().GetResult();
            }
        }
        """;

    [Test]
    public async Task Synchronous_Publishes_Allocate_Nothing_After_Warm_Up(CancellationToken cancellationToken)
    {
        var host = await CompileReleaseHostAsync(Source, cancellationToken);
        await using var provider = BuildProvider(host, static services => services.AddScoped(static _ => new object()));
        await using var scope = provider.CreateAsyncScope();

        var allocated = Call<Dictionary<string, long>>(host, "Sample.Probe", "Measure", scope.ServiceProvider);

        await Assert.That(allocated.Count).IsEqualTo(21);
        var oneBox = allocated["one box"];
        await Assert.That(oneBox).IsGreaterThan(0L);
        foreach (var (scenario, bytes) in allocated)
        {
            // A struct delivered to interface-typed handlers is boxed once per publish, however many such handlers there are (OQ-17).
            var expected = scenario.StartsWith("boxed", StringComparison.Ordinal) || scenario == "one box" ? oneBox : 0L;
            await Assert.That(bytes).IsEqualTo(expected).Because(scenario);
        }
    }
}
