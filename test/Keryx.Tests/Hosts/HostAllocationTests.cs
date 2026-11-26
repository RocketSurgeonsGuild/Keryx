using Keryx.Analyzers.Hosts;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Tests.Hosts;

/// <summary>
///     The Performance contract for <c>Send</c> with synchronous handlers and no behaviors (ADR 0001, Performance contract): after one warm-up
///     call in the same scope, Keryx allocates 0 bytes per call on the typed overloads and, for class requests, on the generic entry point,
///     for value-type and reference-type responses. A struct request sent through <c>ISender</c> is boxed at the call site (KRX021); that box
///     is the caller's, and dispatching an already boxed struct allocates nothing.
/// </summary>
/// <remarks>
///     The measuring loops live in the compiled host (<c>Sample.Probe</c>), so no reflection runs inside the measured region. Each region
///     completes synchronously (<c>IsCompletedSuccessfully</c>, then <c>Result</c>) and is measured with
///     <c>GC.GetAllocatedBytesForCurrentThread</c>, several times, keeping the smallest: runtime caches shared with the rest of the process
///     (generic virtual method and cast caches) can grow on the measuring thread while other code fills them. For the same reason the class
///     runs alone (<c>[NotInParallel]</c> without a key: no other test runs at the same time).
/// </remarks>
[Timeout(ModuleInitializer.TestTimeout)]
[NotInParallel]
public class HostAllocationTests : HostTest
{
    private const string Probe = """

        public static class Probe
        {
            public const int Iterations = 1_000;
            public const int Repeats = 5;

            public static global::System.Collections.Generic.Dictionary<string, long> Measure(IServiceProvider services)
            {
                var mediator = services.GetRequiredService<KeryxMediator>();
                ISender sender = mediator;

                var ping = new Ping();
                var add = new Add(1, 2);
                var echo = new Echo("echo");
                var count = new Count(1);
                var tick = new Tick(1);
                var name = new Name(1);
                var touch = new Touch();
                var done = new Done();
                Base @base = new Base(1);
                IRequest<int> boxedTick = tick;
                IRequest<string> boxedName = name;

                // Each delegate is created before its region, and each region starts with the first call in this scope.
                return new()
                {
                    ["typed class, reference response, static handler"] = Region(() => mediator.Send(ping)),
                    ["typed class, value response, static handler"] = Region(() => mediator.Send(add)),
                    ["typed class, reference response, injected handler"] = Region(() => mediator.Send(echo)),
                    ["typed class, value response, injected handler"] = Region(() => mediator.Send(count)),
                    ["typed struct, value response, injected handler"] = Region(() => mediator.Send(tick)),
                    ["typed struct, reference response, static handler"] = Region(() => mediator.Send(name)),
                    ["typed IRequest, void handler"] = Region(() => mediator.Send(touch)),
                    ["typed IRequest, completed ValueTask handler"] = Region(() => mediator.Send(done)),
                    ["generic class, reference response, static handler"] = Region(() => sender.Send(ping)),
                    ["generic class, value response, static handler"] = Region(() => sender.Send(add)),
                    ["generic class, reference response, injected handler"] = Region(() => sender.Send(echo)),
                    ["generic class, value response, injected handler"] = Region(() => sender.Send(count)),
                    ["generic IRequest, void handler"] = Region(() => sender.Send(touch)),
                    ["generic IRequest, completed ValueTask handler"] = Region(() => sender.Send(done)),
                    ["typed non-sealed class, runtime type check"] = Region(() => mediator.Send(@base)),
                    ["generic non-sealed class"] = Region(() => sender.Send(@base)),
                    ["generic on the mediator class, class request"] = Region(() => mediator.Send<string>(echo)),
                    ["generic struct, boxed by the caller beforehand, value response"] = Region(() => sender.Send(boxedTick)),
                    ["generic struct, boxed by the caller beforehand, reference response"] = Region(() => sender.Send(boxedName)),
                };
            }

            /// <summary>The struct request boxed at each call site of ISender.Send: the caller's allocation, not Keryx's.</summary>
            public static long BoxedAtTheCallSite(IServiceProvider services)
            {
                ISender sender = services.GetRequiredService<KeryxMediator>();
                return Region(() => sender.Send(new Tick(1)));
            }

            public static long Region<T>(Func<ValueTask<T>> send)
            {
                _ = Complete(send());
                var smallest = long.MaxValue;
                for (var repeat = 0; repeat < Repeats; repeat++)
                {
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    for (var i = 0; i < Iterations; i++)
                    {
                        _ = Complete(send());
                    }

                    smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
                }

                return smallest;
            }

            private static T Complete<T>(ValueTask<T> task) =>
                task.IsCompletedSuccessfully ? task.Result : throw new InvalidOperationException("The send did not complete synchronously.");
        }
        """;

    private const string Requests = """
        namespace Sample;

        public sealed class Clock { }

        public sealed record Ping : IRequest<string>;
        public static partial class PingHandler { private static string Handle(Ping request) => "pong"; }

        public sealed record Add(int Left, int Right) : IRequest<int>;
        public static partial class AddHandler { private static int Handle(Add request) => request.Left + request.Right; }

        public sealed record Echo(string Text) : IRequest<string>;
        public partial class EchoHandler { private static ValueTask<string> Handle(Echo request, Clock clock, CancellationToken ct) => new(request.Text); }

        public sealed record Count(int Value) : IRequest<int>;
        public partial class CountHandler { private static int Handle(Count request, Clock clock) => request.Value + 1; }

        public readonly record struct Tick(int Value) : IRequest<int>;
        public partial class TickHandler { private static int Handle(Tick request, Clock clock) => request.Value + 1; }

        public readonly record struct Name(int Value) : IRequest<string>;
        public static partial class NameHandler { private static string Handle(Name request) => "name"; }

        public sealed record Touch : IRequest;
        public static partial class TouchHandler { private static void Handle(Touch request) { } }

        public sealed record Done : IRequest;
        public partial class DoneHandler { private static ValueTask Handle(Done request, Clock clock) => default; }

        /// <summary>Not sealed: its typed overload checks the runtime type first (OQ-31).</summary>
        public record Base(int Value) : IRequest<int>;
        public static partial class BaseHandler { private static int Handle(Base request) => request.Value + 1; }

        public static class Registrations
        {
            public static void Required(IServiceCollection services) => services.AddScoped(static _ => new Clock());
        }
        """;

    [Test]
    public async Task Synchronous_Sends_Allocate_Nothing_After_Warm_Up(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Requests + Probe, cancellationToken);
        await using var provider = BuildProvider(host, HostRuntimeTests.Registrations(host));
        await using var scope = provider.CreateAsyncScope();

        var allocated = Call<Dictionary<string, long>>(host, "Sample.Probe", "Measure", scope.ServiceProvider);

        await Assert.That(allocated.Count).IsEqualTo(19);
        foreach (var (scenario, bytes) in allocated)
        {
            await Assert.That(bytes).IsEqualTo(0L).Because(scenario);
        }
    }

    [Test]
    public async Task A_Struct_Request_Sent_Through_ISender_Is_Boxed_By_The_Caller(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Requests + Probe, cancellationToken);
        await using var provider = BuildProvider(host, HostRuntimeTests.Registrations(host));
        await using var scope = provider.CreateAsyncScope();

        var allocated = Call<long>(host, "Sample.Probe", "BoxedAtTheCallSite", scope.ServiceProvider);

        // Exactly one box of a Tick per call (KRX021): object header and method table pointer plus the int, rounded up to the pointer size
        // (24 bytes on 64-bit). Dispatching the boxed request adds nothing (see the pre-boxed scenarios above).
        var box = ( ( IntPtr.Size * 2 ) + sizeof(int) + IntPtr.Size - 1 ) / IntPtr.Size * IntPtr.Size;
        await Assert.That(allocated).IsEqualTo(1_000L * box);
    }

    [Test]
    public async Task FrozenDictionary_Dispatch_Allocates_Nothing_After_Warm_Up(CancellationToken cancellationToken)
    {
        var count = HostEmitter.DispatchThreshold + 1;
        var last = $"R{count - 1:00}";
        var host = await CompileHostAsync(
            HostGeneratorTests.ManyRequests(count)
          + $$"""

              public static class Probe
              {
                  public static long[] Measure(IServiceProvider services)
                  {
                      ISender sender = services.GetRequiredService<KeryxMediator>();
                      var first = new R00(1);
                      var last = new {{last}}(1);
                      var results = new long[2];
                      results[0] = Region(() => sender.Send(first));
                      results[1] = Region(() => sender.Send(last));
                      return results;
                  }

                  private static long Region(Func<ValueTask<int>> send)
                  {
                      _ = send().Result;
                      var smallest = long.MaxValue;
                      for (var repeat = 0; repeat < 5; repeat++)
                      {
                          var before = GC.GetAllocatedBytesForCurrentThread();
                          for (var i = 0; i < 1_000; i++)
                          {
                              var task = send();
                              _ = task.IsCompletedSuccessfully ? task.Result : throw new InvalidOperationException();
                          }

                          smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
                      }

                      return smallest;
                  }
              }
              """,
            cancellationToken
        );
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();

        await Assert.That(host.GetType("Sample.KeryxMediator")!.GetField("__requestTypes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)).IsNotNull();
        await Assert.That(Call<long[]>(host, "Sample.Probe", "Measure", scope.ServiceProvider)).IsEquivalentTo([0L, 0L]);
    }
}
