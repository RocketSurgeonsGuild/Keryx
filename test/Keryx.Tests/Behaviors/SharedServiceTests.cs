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
///     A pipeline context resolves each service its behavior methods and its handlers inject once (ADR 0001, Injected parameters; owner
///     decisions 9 and 43): one field per distinct (service type, key), named <c>Service0</c>, <c>Service1</c>, ... in a stable ordinal order
///     of (service type, key) (OQ-64), in request, stream and notification pipeline contexts. A service with a required consumer is resolved
///     as required for every consumer; one whose consumers are all optional is resolved once as optional, and each call site applies its own
///     default. A transient shared by several behavior methods or handlers is one instance per pipeline context. The unkeyed
///     <c>IServiceProvider</c> is not a numbered service: the context holds the provider it is built with in its <c>ServiceProvider</c>
///     field, only when a behavior method or an own handler injects it (OQ-63).
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
        // No behavior injects IServiceProvider, so no context holds it.
        await Assert.That(mediator).DoesNotContain("ServiceProvider ServiceProvider;");
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

        // One type, so the key orders them: no key first, then the keys' literal text ordinally ("earth" before "utc"), whatever the order
        // of first use (OQ-64).
        var mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName);
        await Assert.That(Fields(mediator)).IsEquivalentTo(
            ["global::Sample.IClock Service0", "global::Sample.IClock Service1", "global::Sample.IClock Service2"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
        await Assert.That(mediator).Contains("Service0 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Sample.IClock>(serviceProvider);");
        await Assert.That(mediator).Contains("Service1 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService<global::Sample.IClock>(serviceProvider, \"earth\");");
        await Assert.That(mediator).Contains("Service2 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService<global::Sample.IClock>(serviceProvider, \"utc\");");
        await VerifyHost(result);
    }

    [Test]
    public async Task Should_Resolve_A_Service_With_A_Required_Consumer_As_Required_For_All(CancellationToken cancellationToken)
    {
        // The optional consumers come first in the pipeline; the required one still decides the resolution, and every consumer receives the
        // resolved instance as is (no default at the call site). Value types and Nullable<T> share the same way; an IServiceProvider, even
        // an optional one, is always the scope's provider, held in the context's ServiceProvider field (OQ-63).
        var result = await GenerateAsync(
            Behavior("Maybe", 1, "IClock? clock = null, int retries = 3, int? limit = null, IServiceProvider? services = null")
          + Behavior("Must", 2, "IClock clock, int retries, int? limit, IServiceProvider services")
          + Behavior("MaybeAgain", 3, "IClock? clock = null, int retries = 5, int? limit = 9, IServiceProvider? services = null")
          + Ping("Maybe", "Must", "MaybeAgain"),
            cancellationToken
        );

        var mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName);
        await Assert.That(Fields(mediator)).IsEquivalentTo(
            ["global::Sample.IClock Service0", "int Service1", "int? Service2"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
        await Assert.That(mediator).Contains("Service0 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Sample.IClock>(serviceProvider);");
        await Assert.That(mediator).Contains("Service1 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<int>(serviceProvider);");
        await Assert.That(mediator).Contains("Service2 = (int?)global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService(serviceProvider, typeof(int?));");
        // The scope's IServiceProvider is never resolved: the context holds the provider it was built with (open-questions OQ-63).
        await Assert.That(mediator).Contains("public readonly global::System.IServiceProvider ServiceProvider;");
        await Assert.That(mediator).Contains("ServiceProvider = serviceProvider;");
        await Assert.That(Count(mediator, "cancellationToken, _?pipeline\\.Service0, _?pipeline\\.Service1, _?pipeline\\.Service2, _?pipeline\\.ServiceProvider\\)")).IsEqualTo(3);
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

        // One nullable field per service, resolved once with GetService/GetKeyedService; each call site applies its own default. The fields
        // are numbered by service type ordinally (OQ-64), not in the order the behaviors use them.
        var mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName);
        await Assert.That(Fields(mediator)).IsEquivalentTo(
            ["global::Sample.IClock? Service0", "global::System.TimeSpan? Service1", "int? Service2", "int? Service3", "string? Service4"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
        await Assert.That(mediator).Contains("Service0 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<global::Sample.IClock>(serviceProvider);");
        await Assert.That(mediator).Contains("Service2 = serviceProvider.GetService(typeof(int)) is int __value2 ? (int?)__value2 : null;");
        await Assert.That(mediator).Contains("Service3 = serviceProvider.GetService(typeof(int?)) is int __value3 ? (int?)__value3 : null;");
        await Assert.That(mediator).Contains("Service4 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetKeyedService<string>(serviceProvider, \"suffix\");");
        await Assert.That(mediator).Contains(
            "cancellationToken, pipeline.Service4 ?? \"!\", pipeline.Service2 ?? 3, pipeline.Service3, pipeline.Service0, pipeline.Service1.GetValueOrDefault())"
        );
        await Assert.That(mediator).Contains(
            "cancellationToken, pipeline.Service4 ?? \"?\", pipeline.Service2 ?? 5, pipeline.Service3 ?? 7, pipeline.Service0, pipeline.Service1.GetValueOrDefault())"
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
        _ = await mediator.Send((IRequest<int>)Create(host, "Sample.Probe"), cancellationToken);
        await foreach (var _ in mediator.Stream((IStreamRequest<int>)Create(host, "Sample.Feed"), cancellationToken)) { }

        await mediator.Publish((INotification)Create(host, "Sample.Placed"), cancellationToken);

        // Each pipeline context resolved the transient Clock once (and the keyed one once): every behavior of the pipeline saw that
        // instance, on every call in the scope; keyed and unkeyed are different instances. Every IServiceProvider, required or optional,
        // of a request, stream or publish behavior and of a request or notification handler, received the scope's own provider (OQ-63).
        var seen = Seen(host);
        await Assert.That(seen).IsEquivalentTo(
            [
                "request.first 1 utc 2 scope", "request.second 1 scope", "request.third 1 utc 2",
                "request.first 1 utc 2 scope", "request.second 1 scope", "request.third 1 utc 2",
                "request.handler scope",
                "stream.first 3", "stream.second 3 scope",
                "publish.first 4", "publish.second 4 scope", "publish.handler scope",
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
    }

    [Test]
    public async Task Should_Give_A_Service_A_Handler_And_Its_Behaviors_Share_One_Field(CancellationToken cancellationToken)
    {
        var result = await GenerateAsync(LogAndStore + HandlersAndBehaviors, cancellationToken);

        // Owner decision 43: each context resolves a (service type, key) once for its behaviors and its handlers. Ping's handler requires
        // IClock where its behavior takes it optionally, so it is resolved as required for both; the behavior requires IStore where the
        // handler takes it optionally; ILog only the handler injects (optional, a nullable field, held for its static entry). Feed's handler
        // and behavior share one IClock, and so do Placed's behavior and its two handlers (one of them optional).
        var mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName);
        await Assert.That(Fields(mediator)).IsEquivalentTo(
            [
                "global::Sample.IClock Service0", "global::Sample.ILog? Service1", "global::Sample.IStore Service2",
                "global::Sample.IClock Service0",
                "global::Sample.IClock Service0",
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
        await Assert.That(Count(mediator, "GetRequiredService<global::Sample\\.IClock>")).IsEqualTo(3);
        await Assert.That(Count(mediator, "GetRequiredService<global::Sample\\.IStore>")).IsEqualTo(1);
        await Assert.That(Count(mediator, "GetService<global::Sample\\.ILog>")).IsEqualTo(1);
        await Assert.That(mediator).DoesNotContain("HandlerService");
        await Assert.That(mediator).Contains("global::Sample.Maybe.Handle<global::Sample.Ping, int, __Pipeline0.Link1>(request, new(pipeline, request), pipeline.Service2, cancellationToken, pipeline.Service0)");
        await Assert.That(mediator).Contains("global::Sample.PingHandler.__keryx_InvokeAsync(request, pipeline.Service0, pipeline.Service2, pipeline.Service1, cancellationToken)");
        await Assert.That(mediator).Contains("global::Sample.FeedHandler.__keryx_InvokeAsync(request, pipeline.Service0, cancellationToken)");
        await Assert.That(mediator).Contains("global::Sample.OnPlacedA.__keryx_InvokeAsync(notification, Service0, cancellationToken)");
        await Assert.That(mediator).Contains("global::Sample.OnPlacedB.__keryx_InvokeAsync(notification, Service0, cancellationToken)");
        await VerifyHost(result);
    }

    [Test]
    public async Task Handlers_And_Behaviors_Of_One_Context_Receive_The_Same_Instance_Of_A_Transient(CancellationToken cancellationToken)
    {
        // Owner decision 43: a transient service is resolved once per context and shared by the context's behaviors and handlers: a request's
        // own handler and its behavior, a referenced request handler (constructed with the shared instance) and its behavior, a stream's
        // handler and behavior, and a notification's behavior, its two own handlers and a referenced one. Each context is created on first
        // use in the scope, in the order sent (Ping, Fetch, Feed, Placed), so each resolves the next Clock; a new scope resolves new ones.
        var library = await CompileLibraryAsync("Shared", SharedLibrary, cancellationToken);
        var host = await CompileHostAsync(SharedHost, cancellationToken, library);
        var clock = library.Assembly!.GetType("Shared.Clock", throwOnError: true)!;
        await using var provider = BuildProvider(host, services => services.AddTransient(clock));
        _ = Take(library.Assembly!);

        for (var scopeIndex = 0; scopeIndex < 2; scopeIndex++)
        {
            await using var scope = provider.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            for (var call = 0; call < 2; call++)
            {
                _ = await mediator.Send((IRequest<int>)Create(host, "Sample.Ping"), cancellationToken);
                _ = await mediator.Send((IRequest<int>)Create(library.Assembly!, "Shared.Fetch"), cancellationToken);
                await foreach (var _ in mediator.Stream((IStreamRequest<int>)Create(host, "Sample.Feed"), cancellationToken)) { }

                await mediator.Publish((INotification)Create(library.Assembly!, "Shared.Placed"), cancellationToken);
            }
        }

        string[] Calls(int ping, int fetch, int feed, int placed) =>
        [
            $"request.behavior {ping}", $"ping.handler {ping}",
            $"request.behavior {fetch}", $"fetch.handler {fetch}",
            $"stream.behavior {feed}", $"feed.handler {feed}",
            $"publish.behavior {placed}", $"placed.a {placed}", $"placed.b {placed}", $"placed.referenced {placed}",
        ];

        await Assert.That(Take(library.Assembly!)).IsEquivalentTo(
            [.. Calls(1, 2, 3, 4), .. Calls(1, 2, 3, 4), .. Calls(5, 6, 7, 8), .. Calls(5, 6, 7, 8)],
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
        await Assert.That(mediator).Contains("global::Sample.Outer.Handle<global::Sample.Ping, int, __Pipeline0.Link1>(request, new(pipeline, request), pipeline.Service0, cancellationToken)");
        await Assert.That(Fields(mediator)).IsEquivalentTo(Fields(first.GeneratedSources<HostGenerator>()[HostEmitter.MediatorHintName]), TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Adding_A_Behavior_That_Uses_Shared_Services_In_Another_Order_Keeps_The_Context_Services(CancellationToken cancellationToken)
    {
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

        // A global behavior, outermost, that injects IClock and IStore: the order of first use becomes IClock, IStore, ILog, but the set is
        // unchanged, and the services are numbered by (service type, key), so every name stays (OQ-64).
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

        var before = Services(first);
        await Assert.That(before.Select(static s => s.ServiceType)).IsEquivalentTo(
            ["global::Sample.IClock", "global::Sample.ILog", "global::Sample.IStore"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
        await Assert.That(Services(second)).IsEquivalentTo(before, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        var mediator = second.GeneratedSources<HostGenerator>()[HostEmitter.MediatorHintName];
        await Assert.That(Fields(mediator)).IsEquivalentTo(
            ["global::Sample.IClock Service0", "global::Sample.ILog Service1", "global::Sample.IStore Service2"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
        await Assert.That(Fields(first.GeneratedSources<HostGenerator>()[HostEmitter.MediatorHintName])).IsEquivalentTo(
            Fields(mediator),
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
        await Assert.That(mediator).Contains(
            "global::Sample.Outer.Handle<global::Sample.Ping, int, __Pipeline0.Link1>(request, new(pipeline, request), pipeline.Service0, pipeline.Service2, cancellationToken)"
        );
    }

    [Test]
    public async Task Should_Number_The_Services_By_Type_And_Key_Not_By_First_Use(CancellationToken cancellationToken)
    {
        // First use: IStore, keyed ILog "b", ILog, keyed ILog "a", IClock. Stable order: IClock, ILog, ILog "a", ILog "b", IStore. The same
        // set in a request, a stream request and a notification pipeline: each context numbers them the same way.
        const string outer = "IStore store, [FromKeyedServices(\"b\")] ILog b";
        const string middle = "ILog log, [FromKeyedServices(\"a\")] ILog a";
        const string inner = "IClock clock, IStore store";
        var result = await GenerateAsync(
            LogAndStore
          + Behavior("Outer", 1, outer)
          + Behavior("Middle", 2, middle)
          + Behavior("Inner", 3, inner)
          + StreamBehavior("StreamOuter", 1, outer)
          + StreamBehavior("StreamMiddle", 2, middle)
          + StreamBehavior("StreamInner", 3, inner)
          + PublishBehavior("PublishOuter", 1, outer)
          + PublishBehavior("PublishMiddle", 2, middle)
          + PublishBehavior("PublishInner", 3, inner)
          + Ping("Outer", "Middle", "Inner")
          + Feed("StreamOuter", "StreamMiddle", "StreamInner")
          + Placed("PublishOuter", "PublishMiddle", "PublishInner"),
            cancellationToken
        );

        var mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName);
        string[] context =
        [
            "global::Sample.IClock Service0", "global::Sample.ILog Service1", "global::Sample.ILog Service2", "global::Sample.ILog Service3",
            "global::Sample.IStore Service4",
        ];
        await Assert.That(Fields(mediator)).IsEquivalentTo([.. context, .. context, .. context], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(Count(mediator, "Service2 = global::Microsoft\\.Extensions\\.DependencyInjection\\.ServiceProviderKeyedServiceExtensions\\.GetRequiredKeyedService<global::Sample\\.ILog>\\(serviceProvider, \"a\"\\);")).IsEqualTo(3);
        await Assert.That(Count(mediator, "Service3 = global::Microsoft\\.Extensions\\.DependencyInjection\\.ServiceProviderKeyedServiceExtensions\\.GetRequiredKeyedService<global::Sample\\.ILog>\\(serviceProvider, \"b\"\\);")).IsEqualTo(3);
        // Each behavior reads the same slots in all three pipelines (request, stream and publish links).
        await Assert.That(Count(mediator, "cancellationToken, _?pipeline\\.Service4, _?pipeline\\.Service3\\)")).IsEqualTo(3);
        await Assert.That(Count(mediator, "cancellationToken, _?pipeline\\.Service1, _?pipeline\\.Service2\\)")).IsEqualTo(3);
        await Assert.That(Count(mediator, "cancellationToken, _?pipeline\\.Service0, _?pipeline\\.Service4\\)")).IsEqualTo(3);
    }

    [Test]
    public async Task Should_Keep_Keys_Of_Different_Primitive_Types_Apart(CancellationToken cancellationToken)
    {
        // (byte)1 and 1, (short)-1 and -1 are different keys to MS.DI (object.Equals of the boxed values): four services, each key written
        // with its own type, ordered by the key's literal text.
        var result = await GenerateAsync(
            Behavior("Narrow", 1, "[FromKeyedServices((byte)1)] IClock a, [FromKeyedServices((short)-1)] IClock c")
          + Behavior("Wide", 2, "[FromKeyedServices(1)] IClock b, [FromKeyedServices(-1)] IClock d")
          + Ping("Narrow", "Wide"),
            cancellationToken
        );

        var mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName);
        await Assert.That(Fields(mediator)).IsEquivalentTo(
            ["global::Sample.IClock Service0", "global::Sample.IClock Service1", "global::Sample.IClock Service2", "global::Sample.IClock Service3"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
        await Assert.That(mediator).Contains("Service0 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService<global::Sample.IClock>(serviceProvider, (byte)1);");
        await Assert.That(mediator).Contains("Service1 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService<global::Sample.IClock>(serviceProvider, (short)-1);");
        await Assert.That(mediator).Contains("Service2 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService<global::Sample.IClock>(serviceProvider, -1);");
        await Assert.That(mediator).Contains("Service3 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService<global::Sample.IClock>(serviceProvider, 1);");
        await Assert.That(mediator).Contains("cancellationToken, pipeline.Service0, pipeline.Service1)");
        await Assert.That(mediator).Contains("cancellationToken, pipeline.Service3, pipeline.Service2)");
    }

    [Test]
    public async Task Behaviors_And_Handlers_Receive_The_Registration_Of_Their_Own_Key_Type(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(KeyTypes, cancellationToken);
        await using var provider = BuildProvider(
            host,
            services => services.AddKeyedSingleton<string>((byte)1, "byte 1")
                                .AddKeyedSingleton<string>(1, "int 1")
                                .AddKeyedSingleton<string>((short)-1, "short -1")
                                .AddKeyedSingleton<string>(-1, "int -1")
        );
        _ = Seen(host);

        await using var scope = provider.CreateAsyncScope();
        _ = await scope.ServiceProvider.GetRequiredService<ISender>().Send((IRequest<int>)Create(host, "Sample.Ping"), cancellationToken);

        await Assert.That(Seen(host)).IsEquivalentTo(
            ["narrow byte 1, short -1", "wide int 1, int -1", "handler byte 1, int 1"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
    }

    [Test]
    public async Task Should_Hold_The_Scope_Provider_In_One_ServiceProvider_Field(CancellationToken cancellationToken)
    {
        // Two behaviors per pipeline (request, stream, publish) inject the unkeyed IServiceProvider, one required, one optional; the
        // handlers' invokers are static, so the provider is all each context holds.
        var result = await GenerateAsync(
            Behavior("RequestOne", 1, "IServiceProvider services")
          + Behavior("RequestTwo", 2, "IServiceProvider? services = null")
          + """
            [StreamBehavior]
            [BehaviorOrder(Phase.Default, Order = 1)]
            public static class StreamOne
            {
                public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest request, TNext next, IServiceProvider services, CancellationToken cancellationToken)
                    where TRequest : IStreamRequest<TItem>
                    where TNext : struct, IStreamNext<TItem> => next.InvokeAsync(cancellationToken);
            }

            [StreamBehavior]
            [BehaviorOrder(Phase.Default, Order = 2)]
            public static class StreamTwo
            {
                public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest request, TNext next, CancellationToken cancellationToken, IServiceProvider? services = null)
                    where TRequest : IStreamRequest<TItem>
                    where TNext : struct, IStreamNext<TItem> => next.InvokeAsync(cancellationToken);
            }

            [NotificationBehavior]
            [BehaviorOrder(Phase.Default, Order = 1)]
            public static class PublishOne
            {
                public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, IServiceProvider services, CancellationToken cancellationToken)
                    where TNotification : INotification
                    where TNext : struct, INext => next.InvokeAsync(cancellationToken);
            }

            [NotificationBehavior]
            [BehaviorOrder(Phase.Default, Order = 2)]
            public static class PublishTwo
            {
                public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, CancellationToken cancellationToken, IServiceProvider? services = null)
                    where TNotification : INotification
                    where TNext : struct, INext => next.InvokeAsync(cancellationToken);
            }

            public sealed record Feed : IStreamRequest<int>;
            public static partial class FeedHandler
            {
                [Behavior(typeof(StreamOne))]
                [Behavior(typeof(StreamTwo))]
                private static IAsyncEnumerable<int> Handle(Feed request) => throw new NotSupportedException();
            }

            [Behavior(typeof(PublishOne))]
            [Behavior(typeof(PublishTwo))]
            public sealed record Placed : INotification;
            public static partial class OnPlaced { private static ValueTask Handle(Placed notification) => default; }

            """
          + Ping("RequestOne", "RequestTwo"),
            cancellationToken
        );

        // One ServiceProvider field per context, assigned the provider the context is built with (never resolved), read by both calls of
        // each pipeline; no numbered service.
        var mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName);
        await Assert.That(Count(mediator, "public readonly global::System\\.IServiceProvider ServiceProvider;")).IsEqualTo(3);
        await Assert.That(Count(mediator, "ServiceProvider = serviceProvider;")).IsEqualTo(3);
        await Assert.That(Count(mediator, "_?pipeline\\.ServiceProvider[,)]")).IsEqualTo(6);
        await Assert.That(Fields(mediator)).IsEmpty();
        await Assert.That(mediator).DoesNotContain("<global::System.IServiceProvider>");
        await VerifyHost(result);
    }

    [Test]
    public async Task Should_Resolve_A_Keyed_IServiceProvider_As_An_Ordinary_Keyed_Service(CancellationToken cancellationToken)
    {
        var result = await GenerateAsync(
            Behavior("Root", 1, "IServiceProvider services, [FromKeyedServices(\"root\")] IServiceProvider root")
          + Behavior("MaybeRoot", 2, "[FromKeyedServices(\"root\")] IServiceProvider? root = null")
          + Ping("Root", "MaybeRoot"),
            cancellationToken
        );

        // The keyed provider is a numbered service, resolved as required (Root's consumer is) and shared; the unkeyed one is the context's
        // ServiceProvider field.
        var mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName);
        await Assert.That(Fields(mediator)).IsEquivalentTo(["global::System.IServiceProvider Service0"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(mediator).Contains(
            "Service0 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService<global::System.IServiceProvider>(serviceProvider, \"root\");"
        );
        await Assert.That(mediator).Contains("ServiceProvider = serviceProvider;");
        await Assert.That(mediator).Contains("cancellationToken, pipeline.ServiceProvider, pipeline.Service0)");
        await Assert.That(mediator).Contains("cancellationToken, pipeline.Service0)");
    }

    [Test]
    public async Task Should_Pass_The_Scope_Provider_To_A_Handler_That_Injects_It(CancellationToken cancellationToken)
    {
        var result = await GenerateAsync(
            """
            public sealed record Probe : IRequest<int>;
            public partial class ProbeHandler
            {
                private static int Handle(Probe request, IServiceProvider services, [FromKeyedServices("root")] IServiceProvider root) => 1;
            }

            """,
            cancellationToken
        );

        // The handler's services are the context's shared services (owner decision 43): the context holds the scope's provider itself in its
        // ServiceProvider field for the unkeyed parameter (OQ-63) and resolves the keyed one as shared service 0, and passes both to the
        // handler's static entry (owner decision 44). AddKeryx() registers no handler (owner decision 42).
        var mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("public readonly global::System.IServiceProvider ServiceProvider;");
        await Assert.That(mediator).Contains("ServiceProvider = serviceProvider;");
        await Assert.That(mediator).Contains(
            "Service0 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService<global::System.IServiceProvider>(serviceProvider, \"root\");"
        );
        await Assert.That(mediator).Contains("global::Sample.ProbeHandler.__keryx_InvokeAsync(request, pipeline.ServiceProvider, pipeline.Service0, cancellationToken)");
        await Assert.That(Fields(mediator)).IsEquivalentTo(["global::System.IServiceProvider Service0"]);
        await Assert.That(TestSources.HostSource(result, HostEmitter.ServiceCollectionExtensionsHintName)).DoesNotContain("ProbeHandler");
    }

    [Test]
    public async Task Should_Give_Every_Value_Type_Resolution_Of_A_Context_Its_Own_Pattern_Variable(CancellationToken cancellationToken)
    {
        // A context constructor resolves every shared service of its behaviors and handlers in one body (owner decision 43), so each
        // optional value type declares its own __valueN, numbered by its service: a behavior's, an own handler's (static entry) and a
        // referenced handler's (constructed with new) alike. A repeated name would be CS0128 in generated code. Own handlers that inject
        // the same optional service with different defaults share it, and each call applies its own default; a service only a referenced
        // handler's constructor takes is a local of the context's constructor, not a field.
        var library = await CompileLibraryAsync("Values", ValuesLibrary, cancellationToken);
        var result = await HostBuilder.AddCompilationReferences(library).AddSources(Usings + ValuesHost).Build().GenerateAsync(cancellationToken);
        result.AssertCompilationWasSuccessful();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning).Select(static d => d.ToString())).IsEmpty();

        var mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName);
        // The Ping context: the own handler's int retries is shared service 0 (int sorts before int?), the behavior's int? limit service 1.
        await Assert.That(mediator).Contains("Service0 = serviceProvider.GetService(typeof(int)) is int __value0 ? (int?)__value0 : null;");
        await Assert.That(mediator).Contains("Service1 = serviceProvider.GetService(typeof(int?)) is int __value1 ? (int?)__value1 : null;");
        await Assert.That(mediator).Contains("global::Sample.PingHandler.__keryx_InvokeAsync(request, pipeline.Service0 ?? 3, cancellationToken)");
        // The Noted context: the int both own handlers and the first referenced handler inject is held service 0, each call site with its
        // own default (the referenced constructor's included); the long? only the referenced handlers' constructors take is service 1, one
        // local both constructors read, bare for the null default and with ?? for the other.
        await Assert.That(Count(mediator, "Service0 = serviceProvider\\.GetService\\(typeof\\(int\\)\\) is int __value0 \\? \\(int\\?\\)__value0 : null;")).IsEqualTo(2);
        await Assert.That(mediator).Contains("global::Sample.OnNotedA.__keryx_InvokeAsync(notification, Service0 ?? 3, cancellationToken)");
        await Assert.That(mediator).Contains("global::Sample.OnNotedB.__keryx_InvokeAsync(notification, Service0 ?? 4, cancellationToken)");
        await Assert.That(Count(mediator, "var __service1 = serviceProvider\\.GetService\\(typeof\\(long\\?\\)\\) is long __value1 \\? \\(long\\?\\)__value1 : null;")).IsEqualTo(1);
        await Assert.That(mediator).Contains("= new global::Values.NotedHandler(__service1, Service0 ?? 5);");
        await Assert.That(mediator).Contains("= new global::Values.NotedLimitHandler(__service1 ?? 6L);");
        await Assert.That(mediator).DoesNotContain("long? Service1;");
        await Assert.That(mediator).Contains("its referenced handlers and the services its handlers inject");
        await VerifyHost(result);

        // Each call site applies its own default when nothing is registered, and every one receives the registered value otherwise.
        var host = result.Assembly!;
        await Assert.That(await SendAndPublish(host, library.Assembly!, null, cancellationToken))
                    .IsEquivalentTo(["limit none", "own a 3", "own b 4", "referenced none 5", "referenced limit 6", "ping 3"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(
                         await SendAndPublish(
                             host,
                             library.Assembly!,
                             static services => services.AddSingleton(typeof(int), 9).AddSingleton(typeof(int?), 7).AddSingleton(typeof(long?), 5L),
                             cancellationToken
                         )
                     )
                    .IsEquivalentTo(["limit 7", "own a 9", "own b 9", "referenced 5 9", "referenced limit 5", "ping 9"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    /// <summary>
    ///     Sends <c>Ping</c> and publishes <c>Values.Noted</c> once in a new scope of a provider with <paramref name="configure" />, and returns
    ///     what the behavior and the handlers logged, then the response.
    /// </summary>
    private static async Task<string[]> SendAndPublish(
        System.Reflection.Assembly host,
        System.Reflection.Assembly library,
        Action<IServiceCollection>? configure,
        CancellationToken cancellationToken
    )
    {
        _ = Seen(host);
        await using var provider = BuildProvider(host, configure);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var log = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var response = await mediator.Send((IRequest<string>)Create(host, "Sample.Ping"), cancellationToken);
        await mediator.Publish((INotification)Create(library, "Values.Noted", log), cancellationToken);
        return [.. Seen(host), .. log, response];
    }

    /// <summary>
    ///     A library with the <c>Noted</c> notification and two handlers of it: one injects an optional <c>long?</c> with no default and an
    ///     <c>int</c> with a default, the other the same <c>long?</c> with a non-null default.
    /// </summary>
    private const string ValuesLibrary = """
        namespace Values;

        using System.Collections.Concurrent;
        using System.Globalization;
        using System.Threading.Tasks;
        using Keryx;

        public sealed record Noted(ConcurrentQueue<string> Log) : INotification;

        public partial class NotedHandler
        {
            private static ValueTask Handle(Noted notification, long? x = null, int retries = 5)
            {
                notification.Log.Enqueue(
                    "referenced " + (x?.ToString(CultureInfo.InvariantCulture) ?? "none") + " " + retries.ToString(CultureInfo.InvariantCulture)
                );
                return default;
            }
        }

        public partial class NotedLimitHandler
        {
            private static ValueTask Handle(Noted notification, long? limit = 6)
            {
                notification.Log.Enqueue("referenced limit " + (limit?.ToString(CultureInfo.InvariantCulture) ?? "none"));
                return default;
            }
        }
        """;

    /// <summary>
    ///     A host whose <c>Ping</c> has a behavior that injects an optional <c>int?</c> and a handler that injects an <c>int</c> with a
    ///     default, and two own handlers of <c>Values.Noted</c> that inject an <c>int</c> with different defaults.
    /// </summary>
    private const string ValuesHost = """
        namespace Sample;

        using System.Collections.Concurrent;
        using System.Globalization;

        public static class Seen
        {
            private static readonly ConcurrentQueue<string> Lines = new();

            public static void Write(string line) => Lines.Enqueue(line);

            public static string[] Take()
            {
                var lines = Lines.ToArray();
                Lines.Clear();
                return lines;
            }
        }

        [RequestBehavior]
        public static class Limit
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken, int? limit = null)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse>
            {
                Seen.Write("limit " + (limit?.ToString(CultureInfo.InvariantCulture) ?? "none"));
                return next.InvokeAsync(cancellationToken);
            }
        }

        public sealed record Ping : IRequest<string>;

        public partial class PingHandler
        {
            [Behavior(typeof(Limit))]
            private static string Handle(Ping request, int retries = 3) => "ping " + retries.ToString(CultureInfo.InvariantCulture);
        }

        public partial class OnNotedA
        {
            private static ValueTask Handle(Values.Noted notification, int retries = 3)
            {
                notification.Log.Enqueue("own a " + retries.ToString(CultureInfo.InvariantCulture));
                return default;
            }
        }

        public partial class OnNotedB
        {
            private static ValueTask Handle(Values.Noted notification, int retries = 4)
            {
                notification.Log.Enqueue("own b " + retries.ToString(CultureInfo.InvariantCulture));
                return default;
            }
        }
        """;

    /// <summary>
    ///     A request, a stream request and a notification type, each with four behaviors (<c>Order</c> 1 to 4) that all inject <c>IClock</c>,
    ///     and a handler for each.
    /// </summary>
    private static readonly string FourClocks = BuildFourClocks();

    /// <summary>
    ///     A host whose behaviors log the transient <c>Clock</c> instances they receive (<c>Seen</c>): <c>Ping</c> through <c>First</c> (a
    ///     required <c>Clock</c> and a keyed one), <c>Second</c> (an optional <c>Clock</c>) and <c>Third</c>; <c>Feed</c> and <c>Placed</c>
    ///     through two behaviors each; and <c>Lookup</c> through two behaviors whose injected services (keyed and unkeyed, reference and
    ///     value types) are all optional, with different defaults. <c>First</c>, <c>Second</c>, <c>StreamSecond</c> and <c>PublishSecond</c>
    ///     also log whether their <c>IServiceProvider</c> (optional in <c>Second</c>, required in the others) is <c>Seen.Provider</c>, and so do
    ///     the handlers of <c>Probe</c> and <c>OnPlacedWithProvider</c>, which inject it.
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
            public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest request, TNext next, Clock clock, IServiceProvider services, CancellationToken cancellationToken)
                where TRequest : IStreamRequest<TItem>
                where TNext : struct, IStreamNext<TItem>
            {
                Seen.Write("stream.second " + clock + " " + Seen.Which(services));
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
            public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, Clock clock, IServiceProvider services, CancellationToken cancellationToken)
                where TNotification : INotification
                where TNext : struct, INext
            {
                Seen.Write("publish.second " + clock + " " + Seen.Which(services));
                return next.InvokeAsync(cancellationToken);
            }
        }

        [Behavior(typeof(PublishFirst))]
        [Behavior(typeof(PublishSecond))]
        public sealed record Placed : INotification;
        public static partial class OnPlaced { private static ValueTask Handle(Placed notification) => default; }

        public partial class OnPlacedWithProvider
        {
            private static ValueTask Handle(Placed notification, IServiceProvider services)
            {
                Seen.Write("publish.handler " + Seen.Which(services));
                return default;
            }
        }

        public sealed record Probe : IRequest<int>;
        public partial class ProbeHandler
        {
            private static int Handle(Probe request, IServiceProvider services)
            {
                Seen.Write("request.handler " + Seen.Which(services));
                return 1;
            }
        }

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

    /// <summary>
    ///     A request (<c>Ping</c>), a stream request (<c>Feed</c>) and a notification (<c>Placed</c>) whose handlers inject services their
    ///     behaviors inject too, required on one side and optional on the other, and a service only the request's handler injects.
    /// </summary>
    private const string HandlersAndBehaviors = """
        [RequestBehavior]
        public static class Maybe
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, IStore store, CancellationToken cancellationToken, IClock? clock = null)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse> => next.InvokeAsync(cancellationToken);
        }

        [StreamBehavior]
        public static class StreamClock
        {
            public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest request, TNext next, IClock clock, CancellationToken cancellationToken)
                where TRequest : IStreamRequest<TItem>
                where TNext : struct, IStreamNext<TItem> => next.InvokeAsync(cancellationToken);
        }

        [NotificationBehavior]
        public static class PublishClock
        {
            public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, IClock clock, CancellationToken cancellationToken)
                where TNotification : INotification
                where TNext : struct, INext => next.InvokeAsync(cancellationToken);
        }

        public sealed record Ping : IRequest<int>;

        public partial class PingHandler
        {
            [Behavior(typeof(Maybe))]
            private static int Handle(Ping request, IClock clock, IStore? store = null, ILog? log = null) => 1;
        }

        public sealed record Feed : IStreamRequest<int>;

        public partial class FeedHandler
        {
            [Behavior(typeof(StreamClock))]
            private static IAsyncEnumerable<int> Handle(Feed request, IClock clock) => throw new NotSupportedException();
        }

        [Behavior(typeof(PublishClock))]
        public sealed record Placed : INotification;

        public partial class OnPlacedA { private static ValueTask Handle(Placed notification, IClock clock) => default; }

        public partial class OnPlacedB { private static ValueTask Handle(Placed notification, IClock? clock = null) => default; }

        """;

    /// <summary>
    ///     A library with a transient <c>Clock</c> that numbers its instances, the log every consumer writes the instance it received to
    ///     (<c>Seen</c>), the request <c>Fetch</c> and the notification <c>Placed</c>, each with a handler that injects a <c>Clock</c>.
    /// </summary>
    private const string SharedLibrary = """
        namespace Shared;

        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Keryx;

        public sealed class Clock
        {
            private static int _created;
            public int Id { get; } = Interlocked.Increment(ref _created);
            public override string ToString() => Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static class Seen
        {
            private static readonly List<string> Entries = [];
            public static void Write(string entry) { lock (Entries) { Entries.Add(entry); } }
            public static string[] Take() { lock (Entries) { var entries = Entries.ToArray(); Entries.Clear(); return entries; } }
        }

        public sealed record Fetch : IRequest<int>;

        public partial class FetchHandler
        {
            private static int Handle(Fetch request, Clock clock)
            {
                Seen.Write("fetch.handler " + clock);
                return 1;
            }
        }

        public sealed record Placed : INotification;

        public partial class OnPlacedReferenced
        {
            private static ValueTask Handle(Placed notification, Clock clock)
            {
                Seen.Write("placed.referenced " + clock);
                return default;
            }
        }
        """;

    /// <summary>
    ///     A host over <see cref="SharedLibrary" />: a global request behavior, a global stream behavior and a global notification behavior
    ///     that inject the library's <c>Clock</c> (so they apply to the library's <c>Fetch</c> and <c>Placed</c> too), the request <c>Ping</c>,
    ///     the stream request <c>Feed</c> and two own handlers of <c>Placed</c> ordered before the library's, each injecting a <c>Clock</c>.
    /// </summary>
    private const string SharedHost = """
        namespace Sample;

        using Shared;

        [RequestBehavior]
        [GlobalBehavior]
        public static class RequestStamp
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, Clock clock, CancellationToken cancellationToken)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse>
            {
                Seen.Write("request.behavior " + clock);
                return next.InvokeAsync(cancellationToken);
            }
        }

        [StreamBehavior]
        [GlobalBehavior]
        public static class StreamStamp
        {
            public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest request, TNext next, Clock clock, CancellationToken cancellationToken)
                where TRequest : IStreamRequest<TItem>
                where TNext : struct, IStreamNext<TItem>
            {
                Seen.Write("stream.behavior " + clock);
                return next.InvokeAsync(cancellationToken);
            }
        }

        [NotificationBehavior]
        [GlobalBehavior]
        public static class PublishStamp
        {
            public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, Clock clock, CancellationToken cancellationToken)
                where TNotification : INotification
                where TNext : struct, INext
            {
                Seen.Write("publish.behavior " + clock);
                return next.InvokeAsync(cancellationToken);
            }
        }

        public sealed record Ping : IRequest<int>;

        public partial class PingHandler
        {
            private static int Handle(Ping request, Clock clock)
            {
                Seen.Write("ping.handler " + clock);
                return 1;
            }
        }

        public sealed record Feed : IStreamRequest<int>;

        public partial class FeedHandler
        {
            private static async IAsyncEnumerable<int> Handle(Feed request, Clock clock)
            {
                Seen.Write("feed.handler " + clock);
                await Task.Yield();
                yield return 1;
            }
        }

        public partial class OnPlacedA
        {
            [HandlerOrder(-2)]
            private static ValueTask Handle(Placed notification, Clock clock)
            {
                Seen.Write("placed.a " + clock);
                return default;
            }
        }

        public partial class OnPlacedB
        {
            [HandlerOrder(-1)]
            private static ValueTask Handle(Placed notification, Clock clock)
            {
                Seen.Write("placed.b " + clock);
                return default;
            }
        }
        """;

    /// <summary>
    ///     A host whose behaviors and handler inject <c>string</c> services under keys of different primitive types with equal values
    ///     (<c>(byte)1</c> and <c>1</c>, <c>(short)-1</c> and <c>-1</c>), and log what they receive (<c>Seen</c>).
    /// </summary>
    private const string KeyTypes = """
        namespace Sample;

        public static class Seen
        {
            public static readonly List<string> Entries = [];
            public static void Write(string entry) { lock (Entries) { Entries.Add(entry); } }
            public static string[] Take() { lock (Entries) { var entries = Entries.ToArray(); Entries.Clear(); return entries; } }
        }

        [RequestBehavior]
        [BehaviorOrder(Phase.Default, Order = 1)]
        public static class Narrow
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken, [FromKeyedServices((byte)1)] string a, [FromKeyedServices((short)-1)] string c)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse>
            {
                Seen.Write("narrow " + a + ", " + c);
                return next.InvokeAsync(cancellationToken);
            }
        }

        [RequestBehavior]
        [BehaviorOrder(Phase.Default, Order = 2)]
        public static class Wide
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken, [FromKeyedServices(1)] string b, [FromKeyedServices(-1)] string d)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse>
            {
                Seen.Write("wide " + b + ", " + d);
                return next.InvokeAsync(cancellationToken);
            }
        }

        public sealed record Ping : IRequest<int>;
        public partial class PingHandler
        {
            [Behavior(typeof(Narrow))]
            [Behavior(typeof(Wide))]
            private static int Handle(Ping request, [FromKeyedServices((byte)1)] string a, [FromKeyedServices(1)] string b)
            {
                Seen.Write("handler " + a + ", " + b);
                return 1;
            }
        }
        """;

    private Task<GeneratorTestResults> GenerateAsync(string source, CancellationToken cancellationToken) =>
        GenerateCheckedAsync(HostBuilder.AddSources(Usings + Clock + source).Build(), cancellationToken);

    /// <summary>The namespace the test sources declare, and the <c>IClock</c> service.</summary>
    private const string Clock = "namespace Sample;\n\npublic interface IClock { }\n\n";

    /// <summary>The <c>ILog</c> and <c>IStore</c> services.</summary>
    private const string LogAndStore = "public interface ILog { }\n\npublic interface IStore { }\n\n";

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

    /// <summary>A stream behavior <paramref name="name" /> at <c>Order = order</c> whose open behavior method injects <paramref name="services" />.</summary>
    private static string StreamBehavior(string name, int order, string services) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""
              [StreamBehavior]
              [BehaviorOrder(Phase.Default, Order = {{order}})]
              public static class {{name}}
              {
                  public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest request, TNext next, CancellationToken cancellationToken, {{services}})
                      where TRequest : IStreamRequest<TItem>
                      where TNext : struct, IStreamNext<TItem> => next.InvokeAsync(cancellationToken);
              }


              """
        );

    /// <summary>A notification behavior <paramref name="name" /> at <c>Order = order</c> whose open behavior method injects <paramref name="services" />.</summary>
    private static string PublishBehavior(string name, int order, string services) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""
              [NotificationBehavior]
              [BehaviorOrder(Phase.Default, Order = {{order}})]
              public static class {{name}}
              {
                  public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, CancellationToken cancellationToken, {{services}})
                      where TNotification : INotification
                      where TNext : struct, INext => next.InvokeAsync(cancellationToken);
              }


              """
        );

    /// <summary>The <c>Feed</c> stream request and its handler, with the behaviors attached.</summary>
    private static string Feed(params string[] behaviors) =>
        "public sealed record Feed : IStreamRequest<int>;\n\npublic static partial class FeedHandler\n{\n"
      + string.Concat(behaviors.Select(static b => $"    [Behavior(typeof({b}))]\n"))
      + "    private static IAsyncEnumerable<int> Handle(Feed request) => throw new NotSupportedException();\n}\n";

    /// <summary>The <c>Placed</c> notification, with the behaviors attached, and its handler.</summary>
    private static string Placed(params string[] behaviors) =>
        string.Concat(behaviors.Select(static b => $"[Behavior(typeof({b}))]\n"))
      + "public sealed record Placed : INotification;\n\npublic static partial class OnPlaced { private static ValueTask Handle(Placed notification) => default; }\n";

    private static string BuildFourClocks()
    {
        var builder = new StringBuilder();
        for (var k = 1; k <= 4; k++)
        {
            var name = k.ToString(CultureInfo.InvariantCulture);
            builder.Append(Behavior("Request" + name, k, "IClock clock"));
            builder.Append(StreamBehavior("Stream" + name, k, "IClock clock"));
            builder.Append(PublishBehavior("Publish" + name, k, "IClock clock"));
        }

        builder.Append(Ping("Request1", "Request2", "Request3", "Request4"));
        builder.Append(Feed("Stream1", "Stream2", "Stream3", "Stream4"));
        builder.Append(Placed("Publish1", "Publish2", "Publish3", "Publish4"));
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

    private static string[] Take(System.Reflection.Assembly library) => Call<string[]>(library, "Shared.Seen", "Take");

    [GeneratedRegex(@"public readonly (\S+\??\s+Service\d+);")]
    private static partial Regex ServiceField();
}
