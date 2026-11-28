using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Tests.Hosts;

/// <summary>
///     Compiles a host and sends through the generated mediator (ADR 0001, Generated host; the Phase 3 brief): typed and generic paths,
///     value-type and reference-type responses, <c>IRequest</c>, struct requests, the sealed cases of an abstract or closed request base, a
///     request type that is not sealed (KRX024 suppressed) left out of dispatch, <c>null</c> and unknown requests,
///     <c>AddKeryx()</c> registrations and injected parameters.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class HostRuntimeTests : HostTest
{
    internal const string Source = """
        namespace Sample;

        public interface IGreeter { string Greet(string name); }
        public sealed class Greeter(string greeting) : IGreeter { public string Greet(string name) => greeting + " " + name; }

        /// <summary>Counts the instances made, so tests can tell how often the container constructed it.</summary>
        public sealed class Identity
        {
            public static int Created;
            public int Id { get; } = Interlocked.Increment(ref Created);
        }

        public sealed record Ping : IRequest<string>;
        public static partial class PingHandler { private static string Handle(Ping request) => "pong"; }

        public sealed record Add(int Left, int Right) : IRequest<int>;
        public static partial class AddHandler { private static Task<int> Handle(Add request) => Task.FromResult(request.Left + request.Right); }

        public sealed record Greet(string Name) : IRequest<string>;
        public partial class GreetHandler
        {
            private static ValueTask<string> Handle(Greet request, [FromKeyedServices("polite")] IGreeter greeter, CancellationToken ct, string suffix = "!") =>
                new(greeter.Greet(request.Name) + suffix);
        }

        public readonly record struct Tick(int Value) : IRequest<int>;
        public static partial class TickHandler { private static int Handle(Tick request) => request.Value + 1; }

        public readonly record struct Label(int Value) : IRequest<string>;
        public partial class LabelHandler { private static string Handle(Label request, IGreeter greeter) => greeter.Greet(request.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)); }

        public sealed record Touch : IRequest;
        public static partial class TouchHandler
        {
            public static int Touched;

            private static void Handle(Touch request) => Interlocked.Increment(ref Touched);
        }

        public sealed record Wait : IRequest;
        public static partial class WaitHandler { private static async Task Handle(Wait request, CancellationToken ct) => await Task.Yield(); }

        /// <summary>An abstract request base: never a request itself, so each sealed case has its own handler and it has none.</summary>
        public abstract record Animal(string Name) : IRequest<string>;

        public sealed record Dog(string Name) : Animal(Name);
        public static partial class DogHandler { private static string Handle(Dog request) => "dog " + request.Name; }

        public sealed record Cat(string Name) : Animal(Name);
        public static partial class CatHandler { private static string Handle(Cat request) => "cat " + request.Name; }

        public sealed record WhoAmI : IRequest<int>;
        public partial class WhoAmIHandler { private static int Handle(WhoAmI request, Identity identity) => identity.Id; }

        /// <summary>A stream pipeline with a context: its handler yields the <c>Identity</c> it was constructed with.</summary>
        public sealed record CountMe(int Times) : IStreamRequest<int>;
        public partial class CountMeHandler
        {
            private static IAsyncEnumerable<int> Handle(CountMe request, Identity identity) => Repeat(identity.Id, request.Times);

            private static async IAsyncEnumerable<int> Repeat(int value, int times)
            {
                for (var i = 0; i < times; i++)
                {
                    await Task.Yield();
                    yield return value;
                }
            }
        }

        /// <summary>A publish pipeline with a context: its handler records the <c>Identity</c> it was constructed with.</summary>
        public sealed record SeenBy(System.Collections.Concurrent.ConcurrentQueue<int> Ids) : INotification;
        public partial class SeenByHandler
        {
            private static ValueTask Handle(SeenBy notification, Identity identity)
            {
                notification.Ids.Enqueue(identity.Id);
                return default;
            }
        }

        public sealed record Inspect : IRequest<string>;
        public partial class InspectHandler
        {
            private static string Handle(
                Inspect request,
                IServiceProvider services,
                IMediator mediator,
                ISender sender,
                IPublisher publisher,
                int retries = 3,
                int? limit = null,
                [FromKeyedServices("count")] int count = 7,
                [FromKeyedServices("fallback")] IGreeter? fallback = null) =>
                string.Join(
                    "|",
                    retries,
                    limit?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none",
                    count,
                    fallback?.Greet("x") ?? "none",
                    mediator is KeryxMediator && sender is KeryxMediator && publisher is KeryxMediator,
                    services.GetService(typeof(IMediator)) is KeryxMediator
                );
        }

        /// <summary>Generic requests are not requests (KRX012), so the host knows no handler for one.</summary>
        public sealed record Unknown<T> : IRequest<int>;

        /// <summary>The services the handlers need, registered by the tests before <c>AddKeryx()</c>.</summary>
        public static class Registrations
        {
            public static void Required(IServiceCollection services)
            {
                services.AddKeyedSingleton<IGreeter>("polite", new Greeter("hello"));
                services.AddSingleton<IGreeter>(new Greeter("hello"));
                services.AddTransient(static _ => new Identity());
            }

            public static void Optional(IServiceCollection services)
            {
                services.AddSingleton(typeof(int), 5);
                services.AddSingleton(typeof(int?), 9);
                services.AddKeyedSingleton(typeof(int), "count", 11);
                services.AddKeyedSingleton<IGreeter>("fallback", new Greeter("hi"));
            }
        }
        """;

    [Test]
    public async Task Typed_Overloads_Send_Every_Request_Shape(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        await using var provider = BuildProvider(host, Registrations(host));
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService(host.GetType("Sample.KeryxMediator", throwOnError: true)!);

        await Assert.That(await SendTyped<string>(mediator, Create(host, "Sample.Ping"), cancellationToken)).IsEqualTo("pong");
        await Assert.That(await SendTyped<int>(mediator, Create(host, "Sample.Add", 40, 2), cancellationToken)).IsEqualTo(42);
        await Assert.That(await SendTyped<string>(mediator, Create(host, "Sample.Greet", "keryx"), cancellationToken)).IsEqualTo("hello keryx!");
        await Assert.That(await SendTyped<int>(mediator, Create(host, "Sample.Tick", 41), cancellationToken)).IsEqualTo(42);
        await Assert.That(await SendTyped<string>(mediator, Create(host, "Sample.Label", 7), cancellationToken)).IsEqualTo("hello 7");
        await Assert.That(await SendTyped<Unit>(mediator, Create(host, "Sample.Touch"), cancellationToken)).IsEqualTo(Unit.Value);
        await Assert.That(await SendTyped<Unit>(mediator, Create(host, "Sample.Wait"), cancellationToken)).IsEqualTo(Unit.Value);
        await Assert.That(await SendTyped<string>(mediator, Create(host, "Sample.Dog", "rex"), cancellationToken)).IsEqualTo("dog rex");
        await Assert.That(await SendTyped<string>(mediator, Create(host, "Sample.Cat", "tom"), cancellationToken)).IsEqualTo("cat tom");
    }

    [Test]
    public async Task The_Generic_Entry_Point_Dispatches_On_The_Exact_Runtime_Type(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        await using var provider = BuildProvider(host, Registrations(host));
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        await Assert.That(await sender.Send((IRequest<string>)Create(host, "Sample.Ping"), cancellationToken)).IsEqualTo("pong");
        await Assert.That(await sender.Send((IRequest<int>)Create(host, "Sample.Add", 40, 2), cancellationToken)).IsEqualTo(42);
        await Assert.That(await sender.Send((IRequest<string>)Create(host, "Sample.Greet", "keryx"), cancellationToken)).IsEqualTo("hello keryx!");
        await Assert.That(await sender.Send((IRequest<int>)Create(host, "Sample.Tick", 41), cancellationToken)).IsEqualTo(42);
        await Assert.That(await sender.Send((IRequest<string>)Create(host, "Sample.Label", 7), cancellationToken)).IsEqualTo("hello 7");
        await Assert.That(await sender.Send((IRequest<Unit>)Create(host, "Sample.Touch"), cancellationToken)).IsEqualTo(Unit.Value);
        await Assert.That(await sender.Send((IRequest<Unit>)Create(host, "Sample.Wait"), cancellationToken)).IsEqualTo(Unit.Value);

        // Each sealed case of an abstract request base goes to its own handler (ADR 0001, Requests and notifications).
        await Assert.That(await sender.Send((IRequest<string>)Create(host, "Sample.Dog", "rex"), cancellationToken)).IsEqualTo("dog rex");
        await Assert.That(await sender.Send((IRequest<string>)Create(host, "Sample.Cat", "tom"), cancellationToken)).IsEqualTo("cat tom");
    }

    [Test]
    public async Task A_Request_Type_That_Is_Not_Sealed_Is_Not_Dispatched(CancellationToken cancellationToken)
    {
        // KRX024 makes every request type sealed or abstract (or closed); a project can still turn it off. Such a type is never composed
        // (OQ-68): a referenced one is KRX038 in the host (turned off here too, so the host builds), the host's own one is its KRX024. It has
        // no typed overload and no dispatch section, so sending it throws not-found, while its sealed siblings still dispatch.
        var library = await CompileUnsealedLibraryAsync(cancellationToken);
        var result = await HostBuilder.WithLanguageVersion(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview)
                                      .WithAnalyzer<Keryx.Analyzers.Diagnostics.MessageTypeAnalyzer>()
                                      .AddGlobalOption("dotnet_diagnostic.KRX024.severity", "none")
                                      .AddGlobalOption("dotnet_diagnostic.KRX038.severity", "none")
                                      .AddCompilationReferences(library)
                                      .AddSources(
                                           Usings
                                         + """
                                           namespace Sample;

                                           public record Mine(int Id) : IRequest<string>;
                                           public static partial class MineHandler { private static string Handle(Mine request) => "mine"; }

                                           public sealed record Ours(int Id) : IRequest<string>;
                                           public static partial class OursHandler { private static string Handle(Ours request) => "ours " + request.Id; }
                                           """
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);
        result.AssertCompilationWasSuccessful();
        var host = result.Assembly!;
        var open = library.Assembly!;
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        foreach (var type in (Type[])[open.GetType("Open.Get")!, open.GetType("Open.Items")!, host.GetType("Sample.Mine")!])
        {
            await Assert.That(mediator.GetType().GetMethods().Where(m => m.GetParameters() is [var first, ..] && first.ParameterType == type)).IsEmpty();
        }

        var get = Create(open, "Open.Get", 1);
        var notFound = ThrowsSynchronously<KeryxHandlerNotFoundException>(() => Ignore(mediator.Send((IRequest<string>)get, cancellationToken)));
        await Assert.That(notFound.RequestType).IsEqualTo(get.GetType());
        var items = Create(open, "Open.Items", 1);
        var streamNotFound = ThrowsSynchronously<KeryxHandlerNotFoundException>(() => _ = mediator.Stream((IStreamRequest<int>)items, cancellationToken));
        await Assert.That(streamNotFound.RequestType).IsEqualTo(items.GetType());
        var mine = Create(host, "Sample.Mine", 1);
        var ownNotFound = ThrowsSynchronously<KeryxHandlerNotFoundException>(() => Ignore(mediator.Send((IRequest<string>)mine, cancellationToken)));
        await Assert.That(ownNotFound.RequestType).IsEqualTo(mine.GetType());

        await Assert.That(await mediator.Send((IRequest<string>)Create(open, "Open.Ping", 2), cancellationToken)).IsEqualTo("ping 2");
        await Assert.That(await mediator.Send((IRequest<string>)Create(open, "Open.Ship", 3), cancellationToken)).IsEqualTo("ship 3");
        await Assert.That(await mediator.Send((IRequest<string>)Create(open, "Open.Circle", 4), cancellationToken)).IsEqualTo("circle 4");
        await Assert.That(await mediator.Send((IRequest<string>)Create(host, "Sample.Ours", 5), cancellationToken)).IsEqualTo("ours 5");
    }

    /// <summary>A closed request base (C# 15, preview) with two sealed cases, each with its own handler.</summary>
    private static readonly string ClosedShapes = TestSources.ClosedTypeAttribute
                                                + """
                                                  namespace Shapes
                                                  {
                                                      public closed record Shape : IRequest<string>;
                                                      public sealed record Circle(int Radius) : Shape;
                                                      public sealed record Square(int Side) : Shape;
                                                      public static partial class CircleHandler { private static string Handle(Circle request) => "circle " + request.Radius; }
                                                      public static partial class SquareHandler { private static string Handle(Square request) => "square " + request.Side; }
                                                  }
                                                  """;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task The_Sealed_Cases_Of_A_Closed_Request_Base_Go_To_Their_Own_Handlers(bool referenced, CancellationToken cancellationToken)
    {
        // Declared in the host, or in a referenced library (where the closed base and its cases must live together): the base is never a
        // request, and each case is dispatched by its type pattern.
        var library = referenced
            ? await CompileLibraryAsync("Shapes", "using Keryx;\n" + ClosedShapes, cancellationToken, languageVersion: Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview)
            : null;
        var builder = HostBuilder.WithLanguageVersion(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview);
        builder = library is null ? builder.AddSources(Usings + ClosedShapes) : builder.AddCompilationReferences(library).AddSources(Usings + "namespace Sample;\n");
        var result = await builder.Build().GenerateAsync(cancellationToken);
        result.AssertCompilationWasSuccessful();
        var shapes = library?.Assembly ?? result.Assembly!;
        await using var provider = BuildProvider(result.Assembly!);
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        await Assert.That(await sender.Send((IRequest<string>)Create(shapes, "Shapes.Circle", 2), cancellationToken)).IsEqualTo("circle 2");
        await Assert.That(await sender.Send((IRequest<string>)Create(shapes, "Shapes.Square", 3), cancellationToken)).IsEqualTo("square 3");
    }

    [Test]
    public async Task Null_And_Unknown_Requests_Throw_Synchronously(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        await using var provider = BuildProvider(host, Registrations(host));
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        _ = ThrowsSynchronously<ArgumentNullException>(() => Ignore(mediator.Send<int>(null!, cancellationToken)));
        _ = ThrowsSynchronously<ArgumentNullException>(() => Ignore(mediator.Stream<int>(null!, cancellationToken)));
        _ = ThrowsSynchronously<ArgumentNullException>(() => Ignore(mediator.Publish<INotification>(null!, cancellationToken)));

        var unknownType = host.GetType("Sample.Unknown`1", throwOnError: true)!.MakeGenericType(typeof(int));
        var unknown = (IRequest<int>)Activator.CreateInstance(unknownType)!;
        var exception = ThrowsSynchronously<KeryxHandlerNotFoundException>(() => Ignore(mediator.Send(unknown, cancellationToken)));
        await Assert.That(exception.RequestType).IsEqualTo(unknownType);

        // A request type from another assembly the host knows nothing about.
        var foreign = ThrowsSynchronously<KeryxHandlerNotFoundException>(() => Ignore(mediator.Send(new ForeignRequest(), cancellationToken)));
        await Assert.That(foreign.RequestType).IsEqualTo(typeof(ForeignRequest));
        await Assert.That(foreign.Message).Contains("KeryxHost MSBuild property");
    }

    [Test]
    public async Task Unknown_Stream_Requests_Throw_And_Publish_Knows_No_Types_Yet(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        await using var provider = BuildProvider(host, Registrations(host));
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var exception = ThrowsSynchronously<KeryxHandlerNotFoundException>(() => Ignore(mediator.Stream(new ForeignStream(), cancellationToken)));
        await Assert.That(exception.RequestType).IsEqualTo(typeof(ForeignStream));

        var published = mediator.Publish(new ForeignNotification(), cancellationToken);
        await Assert.That(published.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task AddKeryx_Registers_Transient_Mediators_Over_One_Scope_State(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        var services = new ServiceCollection();
        RegistrationsWithTransientIdentityHandlers(host)(services);
        var registered = services.Count;
        AddKeryx(host, services);
        var count = services.Count;
        AddKeryx(host, services);

        // A second call adds nothing.
        await Assert.That(services.Count).IsEqualTo(count);
        var keryx = services.Skip(registered).ToArray();
        await Assert.That(keryx.All(static d => d.ImplementationFactory is not null)).IsTrue();

        // KeryxMediator, IMediator, ISender and IPublisher are transient factories that each create a mediator (owner decision 46); the
        // scope state that holds the pipeline contexts, and the handlers, are scoped.
        var mediatorType = host.GetType("Sample.KeryxMediator", throwOnError: true)!;
        var stateType = mediatorType.GetNestedType("__KeryxScopeState", BindingFlags.NonPublic)!;
        Type[] mediators = [mediatorType, typeof(IMediator), typeof(ISender), typeof(IPublisher)];
        await Assert.That(keryx.Where(d => mediators.Contains(d.ServiceType)).Select(static d => d.Lifetime).ToArray())
                    .IsEquivalentTo([ServiceLifetime.Transient, ServiceLifetime.Transient, ServiceLifetime.Transient, ServiceLifetime.Transient]);
        await Assert.That(keryx.Single(d => d.ServiceType == stateType).Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(keryx.Where(d => !mediators.Contains(d.ServiceType)).All(static d => d.Lifetime == ServiceLifetime.Scoped)).IsTrue();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var whoAmI = (IRequest<int>)Create(host, "Sample.WhoAmI");
        var state = mediatorType.GetField("__state", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // Every resolve is a new mediator of the same class; the mediators of one scope share the scope's state and so its pipeline contexts
        // (the transient handler registered above makes each context construction a new identity), whichever mediator first uses a context.
        int first, firstStream, firstPublish;
        object firstState;
        await using (var scope = provider.CreateAsyncScope())
        {
            var resolved = mediators.Select(t => scope.ServiceProvider.GetRequiredService(t)).Append(scope.ServiceProvider.GetRequiredService(mediatorType)).ToArray();
            await Assert.That(resolved.All(m => m.GetType() == mediatorType)).IsTrue();
            await Assert.That(resolved.Distinct(ReferenceEqualityComparer.Instance).Count()).IsEqualTo(resolved.Length);
            await Assert.That(resolved.All(m => state.GetValue(m) is null)).IsTrue();

            var identities = new List<int>();
            foreach (var mediator in Enumerable.Reverse(resolved))
            {
                identities.Add(await ( (ISender)mediator ).Send(whoAmI, cancellationToken));
            }

            await Assert.That(identities.Distinct()).HasSingleItem();
            first = identities[0];
            firstState = scope.ServiceProvider.GetRequiredService(stateType);
            await Assert.That(resolved.All(m => ReferenceEquals(state.GetValue(m), firstState))).IsTrue();

            // A mediator resolved after the context exists uses it too.
            await Assert.That(await scope.ServiceProvider.GetRequiredService<ISender>().Send(whoAmI, cancellationToken)).IsEqualTo(first);

            // The stream and publish contexts are the scope's too (their accessors come from other emitters): each mediator, whichever
            // streams or publishes first, reaches the same one, and it is not the request pipeline's.
            var streamed = new List<int>();
            var published = new List<int>();
            foreach (var mediator in resolved)
            {
                streamed.Add(await StreamIdentityAsync(host, (ISender)mediator, cancellationToken));
                published.Add(await PublishIdentityAsync(host, (IPublisher)mediator, cancellationToken));
            }

            await Assert.That(streamed.Distinct()).HasSingleItem();
            await Assert.That(published.Distinct()).HasSingleItem();
            await Assert.That(new[] { first, streamed[0], published[0] }.Distinct().Count()).IsEqualTo(3);
            firstStream = streamed[0];
            firstPublish = published[0];
        }

        // Another scope has its own state and contexts.
        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await Assert.That(await mediator.Send(whoAmI, cancellationToken)).IsNotEqualTo(first);
            await Assert.That(state.GetValue(mediator)).IsNotNull().And.IsNotSameReferenceAs(firstState);
            var other = scope.ServiceProvider.GetRequiredService<IPublisher>();
            await Assert.That(await StreamIdentityAsync(host, mediator, cancellationToken)).IsNotEqualTo(firstStream);
            await Assert.That(await PublishIdentityAsync(host, other, cancellationToken)).IsNotEqualTo(firstPublish);
            await Assert.That(state.GetValue(other)).IsSameReferenceAs(state.GetValue(mediator));
        }

        // From the root provider a mediator resolves, as any transient does; under scope validation its first call that needs a pipeline
        // context throws, because the scope state is scoped, for a send, a stream and a publish alike. A pipeline without a context still
        // runs.
        var countMe = (IStreamRequest<int>)Create(host, "Sample.CountMe", 1);
        var seenBy = (INotification)Create(host, "Sample.SeenBy", new System.Collections.Concurrent.ConcurrentQueue<int>());
        foreach (var serviceType in mediators)
        {
            var root = provider.GetRequiredService(serviceType);
            await Assert.That(await ( (ISender)root ).Send((IRequest<string>)Create(host, "Sample.Ping"), cancellationToken)).IsEqualTo("pong").Because(serviceType.Name);
            var exception = ThrowsSynchronously<InvalidOperationException>(() => Ignore(( (ISender)root ).Send(whoAmI, cancellationToken)));
            await Assert.That(exception.Message).Contains("__KeryxScopeState").Because(serviceType.Name);
            var streamException = ThrowsSynchronously<InvalidOperationException>(() => Ignore(( (ISender)root ).Stream(countMe, cancellationToken)));
            await Assert.That(streamException.Message).Contains("__KeryxScopeState").Because(serviceType.Name);
            var publishException = ThrowsSynchronously<InvalidOperationException>(() => Ignore(( (IPublisher)root ).Publish(seenBy, cancellationToken)));
            await Assert.That(publishException.Message).Contains("__KeryxScopeState").Because(serviceType.Name);
            await Assert.That(state.GetValue(root)).IsNull().Because(serviceType.Name);
        }

        // Handlers without injected parameters are never registered; the others are, with factories.
        await Assert.That(services.Any(d => d.ServiceType == host.GetType("Sample.PingHandler"))).IsFalse();
        await Assert.That(services.Single(d => d.ServiceType == host.GetType("Sample.GreetHandler")).ImplementationFactory).IsNotNull();
    }

    [Test]
    public async Task A_Singleton_Taking_A_Mediator_Fails_At_Its_First_Pipeline_Context(CancellationToken cancellationToken)
    {
        // The documented cost of transient mediators (owner decision 46, which supersedes decisions 35 and 38): a singleton that captures
        // KeryxMediator, IMediator, ISender or IPublisher passes ValidateOnBuild and resolves, with a mediator over the root provider; with
        // ValidateScopes its first call that needs a pipeline context throws, because the scope state is scoped. A call through a pipeline
        // without a context needs no state and runs.
        var host = await CompileHostAsync(Source, cancellationToken);
        var services = new ServiceCollection();
        Registrations(host)(services);
        AddKeryx(host, services);
        services.AddSingleton<CapturesMediator>();
        services.AddSingleton<CapturesSender>();
        services.AddSingleton<CapturesPublisher>();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var ping = (IRequest<string>)Create(host, "Sample.Ping");
        var greet = (IRequest<string>)Create(host, "Sample.Greet", "keryx");

        var senders = new (string Name, ISender Sender)[]
        {
            (nameof(CapturesMediator), scope.ServiceProvider.GetRequiredService<CapturesMediator>().Mediator),
            (nameof(CapturesSender), scope.ServiceProvider.GetRequiredService<CapturesSender>().Sender),
        };
        foreach (var (name, sender) in senders)
        {
            await Assert.That(await sender.Send(ping, cancellationToken)).IsEqualTo("pong").Because(name);
            var exception = ThrowsSynchronously<InvalidOperationException>(() => Ignore(sender.Send(greet, cancellationToken)));
            await Assert.That(exception.Message).Contains("__KeryxScopeState").Because(name);
        }

        // A stream with a context fails the same way, through either captured sender.
        var countMe = (IStreamRequest<int>)Create(host, "Sample.CountMe", 1);
        foreach (var (name, sender) in senders)
        {
            var exception = ThrowsSynchronously<InvalidOperationException>(() => Ignore(sender.Stream(countMe, cancellationToken)));
            await Assert.That(exception.Message).Contains("__KeryxScopeState").Because(name);
        }

        // A captured publisher: a notification without a publish pipeline needs no state, one whose publish pipeline has a context throws.
        var publisher = scope.ServiceProvider.GetRequiredService<CapturesPublisher>().Publisher;
        await publisher.Publish(new ForeignNotification(), cancellationToken);
        var seenBy = (INotification)Create(host, "Sample.SeenBy", new System.Collections.Concurrent.ConcurrentQueue<int>());
        var publishException = ThrowsSynchronously<InvalidOperationException>(() => Ignore(publisher.Publish(seenBy, cancellationToken)));
        await Assert.That(publishException.Message).Contains("__KeryxScopeState").Because(nameof(CapturesPublisher));
    }

    [Test]
    public async Task Injected_Parameters_Resolve_Defaults_Keys_And_The_Mediator(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);

        await using (var provider = BuildProvider(host, Registrations(host)))
        {
            await using var scope = provider.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await Assert.That(await sender.Send((IRequest<string>)Create(host, "Sample.Inspect"), cancellationToken)).IsEqualTo("3|none|7|none|True|True");
        }

        await using (var provider = BuildProvider(
                         host,
                         services =>
                         {
                             Registrations(host)(services);
                             Call<object?>(host, "Sample.Registrations", "Optional", services);
                         }
                     ))
        {
            await using var scope = provider.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await Assert.That(await sender.Send((IRequest<string>)Create(host, "Sample.Inspect"), cancellationToken)).IsEqualTo("5|9|11|hi x|True|True");
        }
    }

    [Test]
    public async Task Each_Scope_Creates_Its_Pipeline_Context_Once(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);

        // With a transient handler every context construction (and every per-call resolution) gets a new Identity, so equal identities in
        // one scope prove that the scope built its context once and reused it.
        await using var provider = BuildProvider(host, RegistrationsWithTransientIdentityHandlers(host));
        var whoAmI = (IRequest<int>)Create(host, "Sample.WhoAmI");

        int first, again, other;
        await using (var scope = provider.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            first = await sender.Send(whoAmI, cancellationToken);
            again = await sender.Send(whoAmI, cancellationToken);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            other = await scope.ServiceProvider.GetRequiredService<ISender>().Send(whoAmI, cancellationToken);
        }

        await Assert.That(again).IsEqualTo(first);
        await Assert.That(other).IsNotEqualTo(first);
    }

    [Test]
    public async Task FrozenDictionary_Dispatch_Sends_Each_Request_To_Its_Own_Handler(CancellationToken cancellationToken)
    {
        // Above the threshold the generic entry point looks the index up and switches on it; every arm reinterprets the request with
        // Unsafe.As, and all R{n} share one layout, so a mismatched index or arm would quietly answer with another handler's response.
        var count = Analyzers.Hosts.HostEmitter.DispatchThreshold + 1;
        var host = await CompileHostAsync(HostGeneratorTests.ManyRequests(count), cancellationToken);
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService(host.GetType("Sample.KeryxMediator", throwOnError: true)!);
        var sender = (ISender)mediator;

        await Assert.That(mediator.GetType().GetField("__requestTypes", BindingFlags.NonPublic | BindingFlags.Static)).IsNotNull();
        object one = 1;
        for (var i = 0; i < count; i++)
        {
            var request = (IRequest<int>)Create(host, $"Sample.R{i:00}", one);
            await Assert.That(await sender.Send(request, cancellationToken)).IsEqualTo(1 + i).Because($"R{i:00} through ISender");
        }

        var last = count - 1;
        await Assert.That(await SendTyped<int>(mediator, Create(host, $"Sample.R{last:00}", 1), cancellationToken)).IsEqualTo(1 + last);

        // An unknown type takes the default arm (index -1) and throws before a task is returned.
        var foreign = ThrowsSynchronously<KeryxHandlerNotFoundException>(() => Ignore(sender.Send(new ForeignRequest(), cancellationToken)));
        await Assert.That(foreign.RequestType).IsEqualTo(typeof(ForeignRequest));
    }

    /// <summary>The host's own registrations of the services its handlers need (<c>Sample.Registrations.Required</c>).</summary>
    internal static Action<IServiceCollection> Registrations(Assembly host) => services => Call<object?>(host, "Sample.Registrations", "Required", services);

    /// <summary>
    ///     <see cref="Registrations" />, plus <c>WhoAmIHandler</c>, <c>CountMeHandler</c> and <c>SeenByHandler</c> registered as transient
    ///     before <c>AddKeryx()</c> (which keeps them): each resolution then constructs a new handler with a new <c>Identity</c>, so tests can
    ///     count pipeline context constructions (requests, streams and publishes).
    /// </summary>
    internal static Action<IServiceCollection> RegistrationsWithTransientIdentityHandlers(Assembly host) =>
        services =>
        {
            Registrations(host)(services);
            var identityType = host.GetType("Sample.Identity", throwOnError: true)!;
            foreach (var name in (string[])["Sample.WhoAmIHandler", "Sample.CountMeHandler", "Sample.SeenByHandler"])
            {
                var handlerType = host.GetType(name, throwOnError: true)!;
                services.AddTransient(handlerType, provider => Activator.CreateInstance(handlerType, provider.GetRequiredService(identityType))!);
            }
        };

    /// <summary>Streams <c>Sample.CountMe</c> once through <paramref name="sender" /> and answers the one identity it yields.</summary>
    internal static async Task<int> StreamIdentityAsync(Assembly host, ISender sender, CancellationToken cancellationToken)
    {
        var ids = new List<int>();
        await foreach (var id in sender.Stream((IStreamRequest<int>)Create(host, "Sample.CountMe", 2), cancellationToken))
        {
            ids.Add(id);
        }

        return ids.Distinct().Single();
    }

    /// <summary>Publishes <c>Sample.SeenBy</c> once through <paramref name="publisher" /> and answers the identity its handler recorded.</summary>
    internal static async Task<int> PublishIdentityAsync(Assembly host, IPublisher publisher, CancellationToken cancellationToken)
    {
        var ids = new System.Collections.Concurrent.ConcurrentQueue<int>();
        await publisher.Publish((INotification)Create(host, "Sample.SeenBy", ids), cancellationToken);
        return ids.Single();
    }

    private static async Task<T> SendTyped<T>(object mediator, object request, CancellationToken cancellationToken)
    {
        var overload = mediator.GetType().GetMethod("Send", BindingFlags.Public | BindingFlags.Instance, [request.GetType(), typeof(CancellationToken)])!;
        return await (ValueTask<T>)overload.Invoke(mediator, [request, cancellationToken])!;
    }

    /// <summary>Calls the mediator and asserts that it throws before returning a task: behaviors never see these exceptions.</summary>
    private static TException ThrowsSynchronously<TException>(Action call)
        where TException : Exception
    {
        try
        {
            call();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name} to be thrown synchronously.");
    }

    private static void Ignore<T>(ValueTask<T> task) => _ = task.AsTask();

    private static void Ignore(ValueTask task) => _ = task.AsTask();

    private static void Ignore<T>(IAsyncEnumerable<T> stream) => _ = stream;

    private sealed record ForeignRequest : IRequest<int>;

    private sealed record ForeignStream : IStreamRequest<int>;

    private sealed record ForeignNotification : INotification;

    private sealed class CapturesMediator(IMediator mediator)
    {
        public IMediator Mediator { get; } = mediator;
    }

    private sealed class CapturesSender(ISender sender)
    {
        public ISender Sender { get; } = sender;
    }

    private sealed class CapturesPublisher(IPublisher publisher)
    {
        public IPublisher Publisher { get; } = publisher;
    }
}
