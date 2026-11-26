using System.Reflection;
using Keryx.Tests.Hosts;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Tests.Behaviors;

/// <summary>
///     Compiles a host with behaviors and sends through it (ADR 0001, Behaviors): the three scopes and their de-duplication, constraint
///     matching for assembly and global behaviors, <c>ExcludeBehavior</c>, behaviors that call <c>next</c> twice or not at all, injected
///     behavior constructors, and the same pipeline on the typed overload and the generic entry point.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class PipelineRuntimeTests : HostTest
{
    internal const string Source = """
        [assembly: KeryxHost]
        [assembly: Behavior(typeof(Sample.AssemblyWide<,>))]
        [assembly: Behavior(typeof(Sample.OnlyForCommands<,>))]
        [assembly: ExcludeBehavior(typeof(Sample.Excluded<,>))]

        namespace Sample;

        public static class Log
        {
            public static readonly List<string> Entries = [];

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

        public interface ICommand { }
        public interface IGreeter { string Greet(string name); }
        public sealed class Greeter : IGreeter { public string Greet(string name) => "hello " + name; }

        [BehaviorOrder(Phase.Outermost)]
        public sealed class Outer<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
        {
            public async ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TNext : struct, INext<TResponse>
            {
                Log.Write("outer>");
                var response = await next.InvokeAsync(cancellationToken);
                Log.Write("<outer");
                return response;
            }
        }

        [BehaviorOrder(Phase.Observability)]
        public sealed class AssemblyWide<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
        {
            public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TNext : struct, INext<TResponse>
            {
                Log.Write("assembly");
                return next.InvokeAsync(cancellationToken);
            }
        }

        [GlobalBehavior]
        [BehaviorOrder(Phase.Security)]
        public sealed class GlobalAudit<TRequest, TResponse>(IGreeter greeter, [FromKeyedServices("suffix")] string suffix = "!")
            : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
        {
            public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TNext : struct, INext<TResponse>
            {
                Log.Write(greeter.Greet("global") + suffix);
                return next.InvokeAsync(cancellationToken);
            }
        }

        /// <summary>Applies only to commands: the assembly scope skips it silently for other requests.</summary>
        [BehaviorOrder(Phase.Validation)]
        public sealed class OnlyForCommands<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
            where TRequest : IRequest<TResponse>, ICommand
        {
            public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TNext : struct, INext<TResponse>
            {
                Log.Write("command");
                return next.InvokeAsync(cancellationToken);
            }
        }

        /// <summary>A global behavior the host excludes.</summary>
        [GlobalBehavior]
        public sealed class Excluded<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
        {
            public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TNext : struct, INext<TResponse>
            {
                Log.Write("excluded");
                return next.InvokeAsync(cancellationToken);
            }
        }

        /// <summary>Calls the rest of the pipeline twice.</summary>
        [BehaviorOrder(Phase.Transaction)]
        public sealed class Twice<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
        {
            public async ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TNext : struct, INext<TResponse>
            {
                Log.Write("twice");
                _ = await next.InvokeAsync(cancellationToken);
                return await next.InvokeAsync(cancellationToken);
            }
        }

        /// <summary>A closed behavior that never calls the rest of the pipeline.</summary>
        [BehaviorOrder(Phase.Caching)]
        public sealed class FromCache : IRequestPipelineBehavior<Cached, string>
        {
            public ValueTask<string> Handle<TNext>(Cached request, TNext next, CancellationToken cancellationToken)
                where TNext : struct, INext<string>
            {
                Log.Write("cache hit");
                return new("cached");
            }
        }

        public sealed record Ping(string Text) : IRequest<string>, ICommand;
        public partial class PingHandler
        {
            [Behavior(typeof(Twice<,>))]
            [Behavior(typeof(Outer<,>))]
            [Behavior(typeof(GlobalAudit<,>))]
            private static string Handle(Ping request, IGreeter greeter)
            {
                Log.Write("handler");
                return greeter.Greet(request.Text);
            }
        }

        public sealed record Query(int Value) : IRequest<int>;
        public static partial class QueryHandler
        {
            private static int Handle(Query request)
            {
                Log.Write("handler");
                return request.Value * 2;
            }
        }

        public sealed record Cached : IRequest<string>;
        public static partial class CachedHandler
        {
            [Behavior(typeof(FromCache))]
            private static string Handle(Cached request) => throw new InvalidOperationException("The cache behavior answers.");
        }

        public readonly record struct Tick(int Value) : IRequest<int>, ICommand;
        public static partial class TickHandler
        {
            [Behavior(typeof(Outer<,>))]
            private static int Handle(Tick request)
            {
                Log.Write("handler");
                return request.Value + 1;
            }
        }

        public sealed record Touch : IRequest;
        public static partial class TouchHandler
        {
            [Behavior(typeof(Outer<,>))]
            private static Task Handle(Touch request, CancellationToken cancellationToken)
            {
                Log.Write("handler");
                return Task.CompletedTask;
            }
        }

        public static class Registrations
        {
            public static void Required(IServiceCollection services)
            {
                services.AddSingleton<IGreeter>(new Greeter());
                services.AddKeyedSingleton<string>("suffix", "?");
            }
        }
        """;

    [Test]
    public async Task Every_Scope_Applies_In_Phase_Order_On_Both_Entry_Points(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var _ = provider;
        await using var scope = provider.CreateAsyncScope();
        var mediator = Mediator(host, scope);

        // Method: Outer, Twice, GlobalAudit (also global: once). Assembly: AssemblyWide, OnlyForCommands (Ping is a command). Global:
        // GlobalAudit, Excluded (excluded by the host). Twice calls the handler twice.
        string[] expected = ["outer>", "assembly", "hello global?", "command", "twice", "handler", "handler", "<outer"];

        await Assert.That(await SendTyped<string>(mediator, Create(host, "Sample.Ping", "keryx"), cancellationToken)).IsEqualTo("hello keryx");
        await Assert.That(TakeLog(host)).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        await Assert.That(await sender.Send((IRequest<string>)Create(host, "Sample.Ping", "again"), cancellationToken)).IsEqualTo("hello again");
        await Assert.That(TakeLog(host)).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Assembly_And_Global_Behaviors_That_Do_Not_Fit_Are_Skipped(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var _ = provider;
        await using var scope = provider.CreateAsyncScope();

        // Query is not a command, so OnlyForCommands is skipped; it has no method behaviors.
        await Assert.That(await scope.ServiceProvider.GetRequiredService<ISender>().Send((IRequest<int>)Create(host, "Sample.Query", 21), cancellationToken))
                    .IsEqualTo(42);
        await Assert.That(TakeLog(host)).IsEquivalentTo(["assembly", "hello global?", "handler"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Behavior_Can_Answer_Without_Calling_Next(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var _ = provider;
        await using var scope = provider.CreateAsyncScope();

        await Assert.That(await SendTyped<string>(Mediator(host, scope), Create(host, "Sample.Cached"), cancellationToken)).IsEqualTo("cached");
        await Assert.That(TakeLog(host)).IsEquivalentTo(["assembly", "hello global?", "cache hit"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Struct_Requests_And_IRequest_Run_Their_Pipelines(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var _ = provider;
        await using var scope = provider.CreateAsyncScope();
        var mediator = Mediator(host, scope);

        await Assert.That(await SendTyped<int>(mediator, Create(host, "Sample.Tick", 41), cancellationToken)).IsEqualTo(42);
        await Assert.That(TakeLog(host))
                    .IsEquivalentTo(["outer>", "assembly", "hello global?", "command", "handler", "<outer"], TUnit.Assertions.Enums.CollectionOrdering.Matching);

        await Assert.That(await scope.ServiceProvider.GetRequiredService<ISender>().Send((IRequest<int>)Create(host, "Sample.Tick", 1), cancellationToken)).IsEqualTo(2);
        await Assert.That(TakeLog(host).Length).IsEqualTo(6);

        await Assert.That(await SendTyped<Unit>(mediator, Create(host, "Sample.Touch"), cancellationToken)).IsEqualTo(Unit.Value);
        await Assert.That(TakeLog(host)).IsEquivalentTo(["outer>", "assembly", "hello global?", "handler", "<outer"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Behaviors_Are_Resolved_Once_Per_Scope(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var _ = provider;
        var behaviorType = host.GetType("Sample.GlobalAudit`2", throwOnError: true)!.MakeGenericType(host.GetType("Sample.Ping", throwOnError: true)!, typeof(string));

        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        await Assert.That(first.ServiceProvider.GetRequiredService(behaviorType)).IsSameReferenceAs(first.ServiceProvider.GetRequiredService(behaviorType));
        await Assert.That(first.ServiceProvider.GetRequiredService(behaviorType)).IsNotSameReferenceAs(second.ServiceProvider.GetRequiredService(behaviorType));

        // AddKeryx() registers closed behaviors only, each with a factory; the excluded and the skipped closings are not registered.
        var services = new ServiceCollection();
        AddKeryx(host, services);
        var registered = services.Select(static d => d.ServiceType).Where(static t => t.IsGenericType).Select(static t => t.Name + "[" + string.Join(",", t.GenericTypeArguments.Select(static a => a.Name)) + "]").Order(StringComparer.Ordinal).ToArray();
        await Assert.That(registered)
                    .IsEquivalentTo(
                         [
                             "AssemblyWide`2[Cached,String]",
                             "AssemblyWide`2[Ping,String]",
                             "AssemblyWide`2[Query,Int32]",
                             "AssemblyWide`2[Tick,Int32]",
                             "AssemblyWide`2[Touch,Unit]",
                             "GlobalAudit`2[Cached,String]",
                             "GlobalAudit`2[Ping,String]",
                             "GlobalAudit`2[Query,Int32]",
                             "GlobalAudit`2[Tick,Int32]",
                             "GlobalAudit`2[Touch,Unit]",
                             "OnlyForCommands`2[Ping,String]",
                             "OnlyForCommands`2[Tick,Int32]",
                             "Outer`2[Ping,String]",
                             "Outer`2[Tick,Int32]",
                             "Outer`2[Touch,Unit]",
                             "Twice`2[Ping,String]",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
        await Assert.That(services.All(static d => d.Lifetime == ServiceLifetime.Scoped && d.ImplementationType is null)).IsTrue();
    }

    private async Task<(Assembly Host, ServiceProvider Provider)> StartAsync(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        _ = TakeLog(host);
        return (host, BuildProvider(host, HostRuntimeTests.Registrations(host)));
    }

    private static object Mediator(Assembly host, AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService(host.GetType("Sample.KeryxMediator", throwOnError: true)!);

    private static string[] TakeLog(Assembly host) => Call<string[]>(host, "Sample.Log", "Take");

    internal static async Task<T> SendTyped<T>(object mediator, object request, CancellationToken cancellationToken)
    {
        var overload = mediator.GetType().GetMethod("Send", BindingFlags.Public | BindingFlags.Instance, [request.GetType(), typeof(CancellationToken)])!;
        return await (ValueTask<T>)overload.Invoke(mediator, [request, cancellationToken])!;
    }
}
