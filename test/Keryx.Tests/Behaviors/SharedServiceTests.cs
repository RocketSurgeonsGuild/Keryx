using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Keryx.Analyzers;
using Keryx.Analyzers.Hosts;
using Keryx.Tests.Hosts;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Keryx.Tests.Behaviors;

/// <summary>
///     A pipeline context resolves each service its behavior methods inject once (ADR 0001, Injected parameters): one field per distinct
///     (service type, key), named <c>Service0</c>, <c>Service1</c>, ... in order of first use, in request, stream and notification pipeline
///     contexts. A service with a required consumer is resolved as required for every consumer; one whose consumers are all optional is
///     resolved once as optional, and each call site applies its own default. A transient shared by several behavior methods is one instance
///     per pipeline context.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public partial class SharedServiceTests : HostTest
{
    [Test]
    public async Task Should_Give_A_Service_Four_Behaviors_Share_One_Field(CancellationToken cancellationToken)
    {
        var result = await GenerateAsync(FourClocks, cancellationToken);

        // One IClock field in each of the request, stream and publish pipeline contexts, resolved once each, read by all four calls.
        var mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName);
        await Assert.That(Fields(mediator)).IsEquivalentTo(
            ["global::Sample.IClock Service0", "global::Sample.IClock Service0", "global::Sample.IClock Service0"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
        await Assert.That(Count(mediator, "GetRequiredService<global::Sample\\.IClock>")).IsEqualTo(3);
        await Assert.That(Count(mediator, "cancellationToken, _?pipeline\\.Service0\\)")).IsEqualTo(12);
        await VerifyHost(result);
    }

    [Test]
    public async Task Should_Keep_Keyed_And_Unkeyed_Services_Of_One_Type_Apart(CancellationToken cancellationToken)
    {
        var result = await GenerateAsync(
            Behavior("Local", 1, "IClock clock")
          + Behavior("Utc", 2, "[FromKeyedServices(\"utc\")] IClock clock")
          + Behavior("UtcAgain", 3, "[FromKeyedServices(\"utc\")] IClock clock")
          + Behavior("Earth", 4, "[FromKeyedServices(\"earth\")] IClock clock")
          + Ping("Local", "Utc", "UtcAgain", "Earth"),
            cancellationToken
        );

        var mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName);
        await Assert.That(Fields(mediator)).IsEquivalentTo(
            ["global::Sample.IClock Service0", "global::Sample.IClock Service1", "global::Sample.IClock Service2"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
        await Assert.That(mediator).Contains("Service0 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Sample.IClock>(serviceProvider);");
        await Assert.That(mediator).Contains("Service1 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService<global::Sample.IClock>(serviceProvider, \"utc\");");
        await Assert.That(mediator).Contains("Service2 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService<global::Sample.IClock>(serviceProvider, \"earth\");");
        await VerifyHost(result);
    }

    [Test]
    public async Task Should_Resolve_A_Service_With_A_Required_Consumer_As_Required_For_All(CancellationToken cancellationToken)
    {
        // The optional consumers come first in the pipeline; the required one still decides the resolution, and every consumer receives the
        // resolved instance as is (no default at the call site). Value types and Nullable<T> share the same way; an IServiceProvider, even
        // an optional one, is always the scope's provider.
        var result = await GenerateAsync(
            Behavior("Maybe", 1, "IClock? clock = null, int retries = 3, int? limit = null, IServiceProvider? services = null")
          + Behavior("Must", 2, "IClock clock, int retries, int? limit, IServiceProvider services")
          + Behavior("MaybeAgain", 3, "IClock? clock = null, int retries = 5, int? limit = 9, IServiceProvider? services = null")
          + Ping("Maybe", "Must", "MaybeAgain"),
            cancellationToken
        );

        var mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName);
        await Assert.That(Fields(mediator)).IsEquivalentTo(
            ["global::Sample.IClock Service0", "int Service1", "int? Service2", "global::System.IServiceProvider Service3"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
        await Assert.That(mediator).Contains("Service0 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Sample.IClock>(serviceProvider);");
        await Assert.That(mediator).Contains("Service1 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<int>(serviceProvider);");
        await Assert.That(mediator).Contains("Service2 = (int?)global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService(serviceProvider, typeof(int?));");
        // The scope's IServiceProvider is never resolved: the context holds the provider it was built with (open-questions OQ-63).
        await Assert.That(mediator).Contains("Service3 = serviceProvider;");
        await Assert.That(Count(mediator, "cancellationToken, _?pipeline\\.Service0, _?pipeline\\.Service1, _?pipeline\\.Service2, _?pipeline\\.Service3\\)")).IsEqualTo(3);
        await Assert.That(mediator).DoesNotContain("GetService");
        await VerifyHost(result);
    }

    [Test]
    public async Task Should_Resolve_A_Service_With_Only_Optional_Consumers_Once_And_Apply_Each_Default(CancellationToken cancellationToken)
    {
        var result = await GenerateAsync(
            Behavior("Low", 1, "[FromKeyedServices(\"suffix\")] string suffix = \"!\", int retries = 3, int? limit = null, IClock? clock = null, TimeSpan timeout = default")
          + Behavior("High", 2, "[FromKeyedServices(\"suffix\")] string suffix = \"?\", int retries = 5, int? limit = 7, IClock? clock = null, TimeSpan timeout = default")
          + Ping("Low", "High"),
            cancellationToken
        );

        // One nullable field per service, resolved once with GetService/GetKeyedService; each call site applies its own default.
        var mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName);
        await Assert.That(Fields(mediator)).IsEquivalentTo(
            ["string? Service0", "int? Service1", "int? Service2", "global::Sample.IClock? Service3", "global::System.TimeSpan? Service4"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
        await Assert.That(mediator).Contains("Service0 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetKeyedService<string>(serviceProvider, \"suffix\");");
        await Assert.That(mediator).Contains("Service1 = serviceProvider.GetService(typeof(int)) is int __value1 ? (int?)__value1 : null;");
        await Assert.That(mediator).Contains("Service2 = serviceProvider.GetService(typeof(int?)) is int __value2 ? (int?)__value2 : null;");
        await Assert.That(mediator).Contains("Service3 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<global::Sample.IClock>(serviceProvider);");
        await Assert.That(mediator).Contains(
            "cancellationToken, pipeline.Service0 ?? \"!\", pipeline.Service1 ?? 3, pipeline.Service2, pipeline.Service3, pipeline.Service4.GetValueOrDefault())"
        );
        await Assert.That(mediator).Contains(
            "cancellationToken, _pipeline.Service0 ?? \"?\", _pipeline.Service1 ?? 5, _pipeline.Service2 ?? 7, _pipeline.Service3, _pipeline.Service4.GetValueOrDefault())"
        );
        await VerifyHost(result);
    }

    [Test]
    public async Task Behaviors_Of_One_Pipeline_Receive_The_Same_Instance_Of_A_Transient(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Runtime, cancellationToken);
        await using var provider = BuildProvider(
            host,
            services => services.AddTransient(host.GetType("Sample.Clock", throwOnError: true)!).AddKeyedTransient(host.GetType("Sample.Clock", throwOnError: true)!, "utc")
        );
        _ = Seen(host);

        await using var scope = provider.CreateAsyncScope();
        // The behaviors' IServiceProvider parameters (required in First, optional in Second) compare what they receive with this one.
        host.GetType("Sample.Seen", throwOnError: true)!.GetField("Provider")!.SetValue(null, scope.ServiceProvider);
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        _ = await mediator.Send((IRequest<int>)Create(host, "Sample.Ping"), cancellationToken);
        _ = await mediator.Send((IRequest<int>)Create(host, "Sample.Ping"), cancellationToken);
        await foreach (var _ in mediator.Stream((IStreamRequest<int>)Create(host, "Sample.Feed"), cancellationToken)) { }

        await mediator.Publish((INotification)Create(host, "Sample.Placed"), cancellationToken);

        // Each pipeline context resolved the transient Clock once (and the keyed one once): every behavior of the pipeline saw that
        // instance, on every call in the scope; keyed and unkeyed are different instances. The required and the optional IServiceProvider
        // both received the scope's own provider (open-questions OQ-63).
        var seen = Seen(host);
        await Assert.That(seen).IsEquivalentTo(
            [
                "request.first 1 utc 2 scope", "request.second 1 scope", "request.third 1 utc 2",
                "request.first 1 utc 2 scope", "request.second 1 scope", "request.third 1 utc 2",
                "stream.first 3", "stream.second 3",
                "publish.first 4", "publish.second 4",
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
    }

    [Test]
    public async Task Optional_Consumers_Apply_Their_Own_Defaults_Only_When_The_Service_Is_Missing(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Runtime, cancellationToken);
        var clock = host.GetType("Sample.Clock", throwOnError: true)!;
        _ = Seen(host);

        await using (var missing = BuildProvider(host, services => services.AddTransient(clock).AddKeyedTransient(clock, "utc")))
        {
            await using var scope = missing.CreateAsyncScope();
            _ = await scope.ServiceProvider.GetRequiredService<ISender>().Send((IRequest<int>)Create(host, "Sample.Lookup"), cancellationToken);
        }

        await using (var registered = BuildProvider(
                         host,
                         services => services.AddTransient(clock).AddKeyedTransient(clock, "utc").AddKeyedSingleton("suffix", ".").AddSingleton(typeof(int), 9).AddSingleton(typeof(int?), 4).AddKeyedSingleton(typeof(int), "count", 6)
                     ))
        {
            await using var scope = registered.CreateAsyncScope();
            _ = await scope.ServiceProvider.GetRequiredService<ISender>().Send((IRequest<int>)Create(host, "Sample.Lookup"), cancellationToken);
        }

        await Assert.That(Seen(host)).IsEquivalentTo(
            // Missing: each consumer's own defaults (the keyed int too); registered: the one resolved value for both.
            ["low ! 3 none 1", "high ? 5 7 2", "low . 9 4 6", "high . 9 4 6"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
    }

    [Test]
    public async Task Adding_A_Behavior_That_Injects_A_Shared_Service_Keeps_The_Context_Services(CancellationToken cancellationToken)
    {
        var first = await HostBuilder.WithNullableContext(NullableContextOptions.Enable)
                                     .WithIncrementalTracking()
                                     .AddSource("Host.cs", Usings + Clock + Behavior("Local", 1, "IClock clock") + Ping("Local"))
                                     .Build()
                                     .GenerateAsync(cancellationToken);

        // A global behavior, outermost, that injects the same IClock: the pipeline gains a behavior, not a service.
        var second = await first.RerunWithSourceAsync(
            "Outer.cs",
            Usings
          + """
            namespace Sample;

            [RequestBehavior]
            [GlobalBehavior]
            [BehaviorOrder(Phase.Outermost)]
            public static class Outer
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, IClock clock, CancellationToken cancellationToken)
                    where TRequest : IRequest<TResponse>
                    where TNext : struct, INext<TResponse> => next.InvokeAsync(cancellationToken);
            }
            """,
            cancellationToken
        );

        var before = Services(first);
        var after = Services(second);
        await Assert.That(before).IsEquivalentTo(after, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(before.Single().ServiceType).IsEqualTo("global::Sample.IClock");

        var mediator = second.GeneratedSources<HostGenerator>()[HostEmitter.MediatorHintName];
        await Assert.That(mediator).Contains("global::Sample.Outer.Handle<global::Sample.Ping, int, __Pipeline0.Link1>(request, new __Pipeline0.Link1(pipeline, request), pipeline.Service0, cancellationToken)");
        await Assert.That(Fields(mediator)).IsEquivalentTo(Fields(first.GeneratedSources<HostGenerator>()[HostEmitter.MediatorHintName]), TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Adding_A_Behavior_That_Uses_Shared_Services_In_Another_Order_Renumbers_The_Context_Services(CancellationToken cancellationToken)
    {
        const string LogAndStore = "public interface ILog { }\n\npublic interface IStore { }\n\n";
        var first = await HostBuilder.WithNullableContext(NullableContextOptions.Enable)
                                     .WithIncrementalTracking()
                                     .AddSource(
                                          "Host.cs",
                                          Usings
                                        + Clock
                                        + LogAndStore
                                        + Behavior("UsesClock", 1, "IClock clock")
                                        + Behavior("UsesLog", 2, "ILog log")
                                        + Behavior("UsesStore", 3, "IStore store")
                                        + Ping("UsesClock", "UsesLog", "UsesStore")
                                      )
                                     .Build()
                                     .GenerateAsync(cancellationToken);

        // A global behavior, outermost, that injects IClock and IStore: the set of services and the service used first are unchanged, but
        // fields are numbered in order of first use, so IStore (now used second) and ILog swap names (open-questions OQ-64).
        var second = await first.RerunWithSourceAsync(
            "Outer.cs",
            Usings
          + """
            namespace Sample;

            [RequestBehavior]
            [GlobalBehavior]
            [BehaviorOrder(Phase.Outermost)]
            public static class Outer
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, IClock clock, IStore store, CancellationToken cancellationToken)
                    where TRequest : IRequest<TResponse>
                    where TNext : struct, INext<TResponse> => next.InvokeAsync(cancellationToken);
            }
            """,
            cancellationToken
        );

        await Assert.That(Services(first).Select(static s => s.ServiceType)).IsEquivalentTo(
            ["global::Sample.IClock", "global::Sample.ILog", "global::Sample.IStore"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
        await Assert.That(Services(second).Select(static s => s.ServiceType)).IsEquivalentTo(
            ["global::Sample.IClock", "global::Sample.IStore", "global::Sample.ILog"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );

        await Assert.That(Fields(first.GeneratedSources<HostGenerator>()[HostEmitter.MediatorHintName])).IsEquivalentTo(
            ["global::Sample.IClock Service0", "global::Sample.ILog Service1", "global::Sample.IStore Service2"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
        await Assert.That(Fields(second.GeneratedSources<HostGenerator>()[HostEmitter.MediatorHintName])).IsEquivalentTo(
            ["global::Sample.IClock Service0", "global::Sample.IStore Service1", "global::Sample.ILog Service2"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
    }

    /// <summary>
    ///     A request, a stream request and a notification type, each with four behaviors (<c>Order</c> 1 to 4) that all inject <c>IClock</c>,
    ///     and a handler for each.
    /// </summary>
    private static readonly string FourClocks = BuildFourClocks();

    /// <summary>
    ///     A host whose behaviors log the transient <c>Clock</c> instances they receive (<c>Seen</c>): <c>Ping</c> through <c>First</c> (a
    ///     required <c>Clock</c> and a keyed one), <c>Second</c> (an optional <c>Clock</c>) and <c>Third</c>; <c>Feed</c> and <c>Placed</c>
    ///     through two behaviors each; and <c>Lookup</c> through two behaviors whose injected services (keyed and unkeyed, reference and
    ///     value types) are all optional, with different defaults. <c>First</c> and <c>Second</c> also log whether their
    ///     <c>IServiceProvider</c> (required in <c>First</c>, optional in <c>Second</c>) is <c>Seen.Provider</c>.
    /// </summary>
    private const string Runtime = """
        namespace Sample;

        public static class Seen
        {
            public static readonly List<string> Entries = [];
            public static object? Provider;
            public static string Which(IServiceProvider? services) => services is null ? "null" : ReferenceEquals(services, Provider) ? "scope" : "other";
            public static void Write(string entry) { lock (Entries) { Entries.Add(entry); } }
            public static string[] Take() { lock (Entries) { var entries = Entries.ToArray(); Entries.Clear(); return entries; } }
        }

        /// <summary>A transient service: numbers its instances.</summary>
        public sealed class Clock
        {
            public static int Created;
            public int Id { get; } = Interlocked.Increment(ref Created);
            public override string ToString() => Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        [RequestBehavior]
        [BehaviorOrder(Phase.Default, Order = 1)]
        public static class First
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, Clock clock, [FromKeyedServices("utc")] Clock utc, IServiceProvider services, CancellationToken cancellationToken)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse>
            {
                Seen.Write("request.first " + clock + " utc " + utc + " " + Seen.Which(services));
                return next.InvokeAsync(cancellationToken);
            }
        }

        [RequestBehavior]
        [BehaviorOrder(Phase.Default, Order = 2)]
        public static class Second
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken, Clock? clock = null, IServiceProvider? services = null)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse>
            {
                Seen.Write("request.second " + clock + " " + Seen.Which(services));
                return next.InvokeAsync(cancellationToken);
            }
        }

        [RequestBehavior]
        [BehaviorOrder(Phase.Default, Order = 3)]
        public static class Third
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, [FromKeyedServices("utc")] Clock utc, Clock clock, TNext next, CancellationToken cancellationToken)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse>
            {
                Seen.Write("request.third " + clock + " utc " + utc);
                return next.InvokeAsync(cancellationToken);
            }
        }

        public sealed record Ping : IRequest<int>;
        public static partial class PingHandler
        {
            [Behavior(typeof(First))]
            [Behavior(typeof(Second))]
            [Behavior(typeof(Third))]
            private static int Handle(Ping request) => 1;
        }

        [StreamBehavior]
        [BehaviorOrder(Phase.Default, Order = 1)]
        public static class StreamFirst
        {
            public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest request, TNext next, Clock clock, CancellationToken cancellationToken)
                where TRequest : IStreamRequest<TItem>
                where TNext : struct, IStreamNext<TItem>
            {
                Seen.Write("stream.first " + clock);
                return next.InvokeAsync(cancellationToken);
            }
        }

        [StreamBehavior]
        [BehaviorOrder(Phase.Default, Order = 2)]
        public static class StreamSecond
        {
            public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest request, TNext next, Clock clock, CancellationToken cancellationToken)
                where TRequest : IStreamRequest<TItem>
                where TNext : struct, IStreamNext<TItem>
            {
                Seen.Write("stream.second " + clock);
                return next.InvokeAsync(cancellationToken);
            }
        }

        public sealed record Feed : IStreamRequest<int>;
        public static partial class FeedHandler
        {
            [Behavior(typeof(StreamFirst))]
            [Behavior(typeof(StreamSecond))]
            private static async IAsyncEnumerable<int> Handle(Feed request)
            {
                await Task.Yield();
                yield return 1;
            }
        }

        [NotificationBehavior]
        [BehaviorOrder(Phase.Default, Order = 1)]
        public static class PublishFirst
        {
            public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, Clock clock, CancellationToken cancellationToken)
                where TNotification : INotification
                where TNext : struct, INext
            {
                Seen.Write("publish.first " + clock);
                return next.InvokeAsync(cancellationToken);
            }
        }

        [NotificationBehavior]
        [BehaviorOrder(Phase.Default, Order = 2)]
        public static class PublishSecond
        {
            public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, Clock clock, CancellationToken cancellationToken)
                where TNotification : INotification
                where TNext : struct, INext
            {
                Seen.Write("publish.second " + clock);
                return next.InvokeAsync(cancellationToken);
            }
        }

        [Behavior(typeof(PublishFirst))]
        [Behavior(typeof(PublishSecond))]
        public sealed record Placed : INotification;
        public static partial class OnPlaced { private static ValueTask Handle(Placed notification) => default; }

        [RequestBehavior]
        [BehaviorOrder(Phase.Default, Order = 1)]
        public static class Low
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(
                TRequest request,
                TNext next,
                CancellationToken cancellationToken,
                [FromKeyedServices("suffix")] string suffix = "!",
                int retries = 3,
                int? limit = null,
                [FromKeyedServices("count")] int count = 1)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse>
            {
                Seen.Write("low " + suffix + " " + retries + " " + (limit?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none") + " " + count);
                return next.InvokeAsync(cancellationToken);
            }
        }

        [RequestBehavior]
        [BehaviorOrder(Phase.Default, Order = 2)]
        public static class High
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(
                TRequest request,
                TNext next,
                CancellationToken cancellationToken,
                [FromKeyedServices("suffix")] string suffix = "?",
                int retries = 5,
                int? limit = 7,
                [FromKeyedServices("count")] int count = 2)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse>
            {
                Seen.Write("high " + suffix + " " + retries + " " + (limit?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none") + " " + count);
                return next.InvokeAsync(cancellationToken);
            }
        }

        public sealed record Lookup : IRequest<int>;
        public static partial class LookupHandler
        {
            [Behavior(typeof(Low))]
            [Behavior(typeof(High))]
            private static int Handle(Lookup request) => 1;
        }
        """;

    private Task<GeneratorTestResults> GenerateAsync(string source, CancellationToken cancellationToken) =>
        GenerateCheckedAsync(HostBuilder.AddSources(Usings + Clock + source).Build(), cancellationToken);

    /// <summary>The namespace the test sources declare, and the <c>IClock</c> service.</summary>
    private const string Clock = "namespace Sample;\n\npublic interface IClock { }\n\n";

    private static async Task<GeneratorTestResults> GenerateCheckedAsync(GeneratorTestContext context, CancellationToken cancellationToken)
    {
        var result = await context.GenerateAsync(cancellationToken);
        result.AssertCompilationWasSuccessful();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning).Select(static d => d.ToString())).IsEmpty();
        return result;
    }

    /// <summary>A request behavior <paramref name="name" /> at <c>Order = order</c> whose open behavior method injects <paramref name="services" />.</summary>
    private static string Behavior(string name, int order, string services) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""
              [RequestBehavior]
              [BehaviorOrder(Phase.Default, Order = {{order}})]
              public static class {{name}}
              {
                  public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken, {{services}})
                      where TRequest : IRequest<TResponse>
                      where TNext : struct, INext<TResponse> => next.InvokeAsync(cancellationToken);
              }


              """
        );

    /// <summary>The <c>Ping</c> request and its handler, with the behaviors attached.</summary>
    private static string Ping(params string[] behaviors) =>
        "public sealed record Ping : IRequest<int>;\n\npublic static partial class PingHandler\n{\n"
      + string.Concat(behaviors.Select(static b => $"    [Behavior(typeof({b}))]\n"))
      + "    private static int Handle(Ping request) => 1;\n}\n";

    private static string BuildFourClocks()
    {
        var builder = new StringBuilder();
        for (var k = 1; k <= 4; k++)
        {
            builder.Append(Behavior("Request" + k.ToString(CultureInfo.InvariantCulture), k, "IClock clock"));
            builder.Append(
                CultureInfo.InvariantCulture,
                $$"""
                  [StreamBehavior]
                  [BehaviorOrder(Phase.Default, Order = {{k}})]
                  public static class Stream{{k}}
                  {
                      public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest request, TNext next, CancellationToken cancellationToken, IClock clock)
                          where TRequest : IStreamRequest<TItem>
                          where TNext : struct, IStreamNext<TItem> => next.InvokeAsync(cancellationToken);
                  }

                  [NotificationBehavior]
                  [BehaviorOrder(Phase.Default, Order = {{k}})]
                  public static class Publish{{k}}
                  {
                      public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, CancellationToken cancellationToken, IClock clock)
                          where TNotification : INotification
                          where TNext : struct, INext => next.InvokeAsync(cancellationToken);
                  }


                  """
            );
        }

        builder.Append(Ping("Request1", "Request2", "Request3", "Request4"));
        builder.Append(
            """

            public sealed record Feed : IStreamRequest<int>;
            public static partial class FeedHandler
            {
                [Behavior(typeof(Stream1))]
                [Behavior(typeof(Stream2))]
                [Behavior(typeof(Stream3))]
                [Behavior(typeof(Stream4))]
                private static IAsyncEnumerable<int> Handle(Feed request) => throw new NotSupportedException();
            }

            [Behavior(typeof(Publish1))]
            [Behavior(typeof(Publish2))]
            [Behavior(typeof(Publish3))]
            [Behavior(typeof(Publish4))]
            public sealed record Placed : INotification;
            public static partial class OnPlaced { private static ValueTask Handle(Placed notification) => default; }
            """
        );
        return builder.ToString();
    }

    /// <summary>Every shared service field the generated mediator declares (<c>type ServiceN</c>), in source order.</summary>
    private static string[] Fields(string mediator) => [.. ServiceField().Matches(mediator).Select(static m => m.Groups[1].Value)];

    /// <summary>How many times <paramref name="pattern" /> (a regular expression) matches <paramref name="text" />.</summary>
    private static int Count(string text, string pattern) => Regex.Count(text, pattern);

    /// <summary>The services the <c>Ping</c> pipeline's context shares, from the composed host model.</summary>
    private static ContextServiceModel[] Services(GeneratorTestResults results)
    {
        var model = (HostModel)results.RunResult<HostGenerator>().TrackedSteps[TrackingNames.HostSources].Single().Outputs.Single().Value;
        return [.. model.Handlers.Single(static h => h.MessageType == "global::Sample.Ping").ContextServices];
    }

    private static string[] Seen(System.Reflection.Assembly host) => Call<string[]>(host, "Sample.Seen", "Take");

    [GeneratedRegex(@"public readonly (\S+\??\s+Service\d+);")]
    private static partial Regex ServiceField();
}
