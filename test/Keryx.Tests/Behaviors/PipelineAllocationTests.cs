using System.Globalization;
using System.Text;
using Keryx.Tests.Hosts;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Tests.Behaviors;

/// <summary>
///     The Performance contract with behaviors (ADR 0001, Performance contract; the Phase 4 brief): after one warm-up call in the same scope,
///     a synchronous <c>Send</c> allocates 0 bytes at depths 1, 3 and 8, on the typed overload and the generic entry point, for value-type and
///     reference-type responses, with static behaviors that take an injected service and pass <c>next</c>'s task on and with <c>async</c>
///     behaviors that complete synchronously (compiled with optimizations, as a Release build ships them). It also covers pipelines without a
///     context (OQ-65): a static handler whose behaviors inject nothing, with <c>async</c> behaviors and with synchronous pass-through ones,
///     on both entry points.
/// </summary>
/// <remarks>Measured like <see cref="HostAllocationTests" />: inside the compiled host, smallest of several regions, not in parallel.</remarks>
[Timeout(ModuleInitializer.TestTimeout)]
[NotInParallel]
public class PipelineAllocationTests : HostTest
{
    [Test]
    [Arguments(1)]
    [Arguments(3)]
    [Arguments(8)]
    public async Task Synchronous_Pipelines_Allocate_Nothing_After_Warm_Up(int depth, CancellationToken cancellationToken)
    {
        // Release, as an application ships: the async behaviors' own state machines are then structs (see CompileReleaseHostAsync).
        var host = await CompileReleaseHostAsync(Source(depth), cancellationToken);
        await using var provider = BuildProvider(host, static services => services.AddScoped(static _ => new object()));
        await using var scope = provider.CreateAsyncScope();

        var allocated = Call<Dictionary<string, long>>(host, "Sample.Probe", "Measure", scope.ServiceProvider);

        await Assert.That(allocated.Count).IsEqualTo(10);
        foreach (var (scenario, bytes) in allocated)
        {
            await Assert.That(bytes).IsEqualTo(0L).Because($"depth {depth}: {scenario}");
        }
    }

    /// <summary>
    ///     A host with <paramref name="depth" /> behaviors on each request: pass-through behaviors that inject a service for <c>Ping</c>
    ///     (reference response, injected handler), <c>Add</c> (value response, static handler) and the struct <c>Tick</c>; <c>async</c> behaviors that complete synchronously
    ///     for <c>Echo</c>; pass-through behaviors that inject nothing for <c>Hop</c> (static handler: with <c>Echo</c>, a pipeline without a
    ///     context); and <c>Touch</c> (<c>IRequest</c>, void handler).
    /// </summary>
    private static string Source(int depth)
    {
        var builder = new StringBuilder("namespace Sample;\n\n");
        for (var k = 1; k <= depth; k++)
        {
            builder.Append(
                CultureInfo.InvariantCulture,
                $$"""
                  [RequestBehavior]
                  [BehaviorOrder(Phase.Default, Order = {{k}})]
                  public static class Pass{{k}}
                  {
                      public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, object service, CancellationToken cancellationToken)
                          where TRequest : IRequest<TResponse>
                          where TNext : struct, INext<TResponse>
                      {
                          GC.KeepAlive(service);
                          return next.InvokeAsync(cancellationToken);
                      }
                  }

                  [RequestBehavior]
                  [BehaviorOrder(Phase.Default, Order = {{k}})]
                  public static class Bare{{k}}
                  {
                      public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                          where TRequest : IRequest<TResponse>
                          where TNext : struct, INext<TResponse>
                          => next.InvokeAsync(cancellationToken);
                  }

                  [RequestBehavior]
                  [BehaviorOrder(Phase.Default, Order = {{k}})]
                  public static class Await{{k}}
                  {
                      public static async ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                          where TRequest : IRequest<TResponse>
                          where TNext : struct, INext<TResponse>
                      {
                          var response = await next.InvokeAsync(cancellationToken);
                          return response;
                      }
                  }


                  """
            );
        }

        string Attributes(string prefix)
        {
            var attributes = new StringBuilder();
            for (var k = 1; k <= depth; k++)
            {
                attributes.Append(CultureInfo.InvariantCulture, $"    [Behavior(typeof({prefix}{k}))]\n");
            }

            return attributes.ToString();
        }

        builder.Append(
            $$"""
              public sealed record Ping : IRequest<string>;
              public partial class PingHandler
              {
              {{Attributes("Pass")}}    private static string Handle(Ping request, object service) => "pong";
              }

              public sealed record Add(int Left, int Right) : IRequest<int>;
              public static partial class AddHandler
              {
              {{Attributes("Pass")}}    private static int Handle(Add request) => request.Left + request.Right;
              }

              public readonly record struct Tick(int Value) : IRequest<int>;
              public static partial class TickHandler
              {
              {{Attributes("Pass")}}    private static int Handle(Tick request) => request.Value + 1;
              }

              public sealed record Echo(string Text) : IRequest<string>;
              public static partial class EchoHandler
              {
              {{Attributes("Await")}}    private static ValueTask<string> Handle(Echo request) => new(request.Text);
              }

              public sealed record Hop(int Value) : IRequest<int>;
              public static partial class HopHandler
              {
              {{Attributes("Bare")}}    private static int Handle(Hop request) => request.Value;
              }

              public sealed record Touch : IRequest;
              public static partial class TouchHandler
              {
              {{Attributes("Pass")}}    private static void Handle(Touch request) { }
              }

              public static class Probe
              {
                  public static Dictionary<string, long> Measure(IServiceProvider services)
                  {
                      var mediator = services.GetRequiredService<KeryxMediator>();
                      ISender sender = mediator;
                      var ping = new Ping();
                      var add = new Add(1, 2);
                      var tick = new Tick(1);
                      var echo = new Echo("echo");
                      var hop = new Hop(1);
                      var touch = new Touch();

                      return new()
                      {
                          ["typed class, reference response, injected handler"] = Region(() => mediator.Send(ping)),
                          ["typed class, value response, static handler"] = Region(() => mediator.Send(add)),
                          ["typed struct, value response"] = Region(() => mediator.Send(tick)),
                          ["typed class, async behaviors completing synchronously"] = Region(() => mediator.Send(echo)),
                          ["typed class, pass-through behaviors that inject nothing (no context)"] = Region(() => mediator.Send(hop)),
                          ["typed IRequest, void handler"] = Region(() => mediator.Send(touch)),
                          ["generic class, reference response"] = Region(() => sender.Send(ping)),
                          ["generic class, value response"] = Region(() => sender.Send(add)),
                          ["generic class, async behaviors completing synchronously (no context)"] = Region(() => sender.Send(echo)),
                          ["generic class, pass-through behaviors that inject nothing (no context)"] = Region(() => sender.Send(hop)),
                      };
                  }

                  private static long Region<T>(Func<ValueTask<T>> send)
                  {
                      _ = Complete(send());
                      var smallest = long.MaxValue;
                      for (var repeat = 0; repeat < 5; repeat++)
                      {
                          var before = GC.GetAllocatedBytesForCurrentThread();
                          for (var i = 0; i < 1_000; i++)
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
              """
        );
        return builder.ToString();
    }
}
