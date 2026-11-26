using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Tests.Hosts;

/// <summary>
///     Compiles a host and sends through the generated mediator (ADR 0001, Generated host; the Phase 3 brief): typed and generic paths,
///     value-type and reference-type responses, <c>IRequest</c>, struct requests, exact-type dispatch, <c>null</c> and unknown requests,
///     <c>AddKeryx()</c> registrations and injected parameters.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class HostRuntimeTests : HostTest
{
    internal const string Source = """
        [assembly: KeryxHost]

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

        public record Animal(string Name) : IRequest<string>;
        public static partial class AnimalHandler { private static string Handle(Animal request) => "animal " + request.Name; }

        public sealed record Dog(string Name) : Animal(Name);
        public static partial class DogHandler { private static string Handle(Dog request) => "dog " + request.Name; }

        public sealed record WhoAmI : IRequest<int>;
        public partial class WhoAmIHandler { private static int Handle(WhoAmI request, Identity identity) => identity.Id; }

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
                    ReferenceEquals(mediator, sender) && ReferenceEquals(sender, publisher),
                    ReferenceEquals(services.GetService(typeof(IMediator)), mediator)
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
        await Assert.That(await SendTyped<string>(mediator, Create(host, "Sample.Animal", "rex"), cancellationToken)).IsEqualTo("animal rex");
        await Assert.That(await SendTyped<string>(mediator, Create(host, "Sample.Dog", "rex"), cancellationToken)).IsEqualTo("dog rex");
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

        // A derived request goes to its own handler, never to the base request's (ADR 0001, Dispatch is by exact runtime type).
        await Assert.That(await sender.Send((IRequest<string>)Create(host, "Sample.Animal", "rex"), cancellationToken)).IsEqualTo("animal rex");
        await Assert.That(await sender.Send((IRequest<string>)Create(host, "Sample.Dog", "rex"), cancellationToken)).IsEqualTo("dog rex");
    }

    [Test]
    public async Task A_Typed_Overload_Sends_A_Derived_Request_To_Its_Own_Handler(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        await using var provider = BuildProvider(host, Registrations(host));
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService(host.GetType("Sample.KeryxMediator", throwOnError: true)!);

        // The Animal overload, called with a Dog (statically typed as an Animal), still dispatches on the runtime type (OQ-31).
        var animalOverload = mediator.GetType().GetMethod("Send", [host.GetType("Sample.Animal")!, typeof(CancellationToken)])!;
        var response = (ValueTask<string>)animalOverload.Invoke(mediator, [Create(host, "Sample.Dog", "rex"), cancellationToken])!;

        await Assert.That(await response).IsEqualTo("dog rex");
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
        await Assert.That(foreign.Message).Contains("[assembly: KeryxHost]");
    }

    [Test]
    public async Task Stream_And_Publish_Know_No_Types_Yet(CancellationToken cancellationToken)
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
    public async Task AddKeryx_Registers_One_Scoped_Mediator_Behind_Every_Interface(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        var services = new ServiceCollection();
        Registrations(host)(services);
        var registered = services.Count;
        AddKeryx(host, services);
        var count = services.Count;
        AddKeryx(host, services);

        // A second call adds nothing.
        await Assert.That(services.Count).IsEqualTo(count);
        var keryx = services.Skip(registered).ToArray();
        await Assert.That(keryx.All(static d => d.Lifetime == ServiceLifetime.Scoped && d.ImplementationFactory is not null)).IsTrue();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var mediatorType = host.GetType("Sample.KeryxMediator", throwOnError: true)!;
        var mediator = first.ServiceProvider.GetRequiredService(mediatorType);

        await Assert.That(first.ServiceProvider.GetRequiredService<IMediator>()).IsSameReferenceAs(mediator);
        await Assert.That(first.ServiceProvider.GetRequiredService<ISender>()).IsSameReferenceAs(mediator);
        await Assert.That(first.ServiceProvider.GetRequiredService<IPublisher>()).IsSameReferenceAs(mediator);
        await Assert.That(second.ServiceProvider.GetRequiredService<IMediator>()).IsNotSameReferenceAs(mediator);
        await Assert.That(() => provider.GetRequiredService<IMediator>()).Throws<InvalidOperationException>();

        // Handlers without injected parameters are never registered; the others are, with factories.
        await Assert.That(services.Any(d => d.ServiceType == host.GetType("Sample.PingHandler"))).IsFalse();
        await Assert.That(services.Single(d => d.ServiceType == host.GetType("Sample.GreetHandler")).ImplementationFactory).IsNotNull();
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
        await using var provider = BuildProvider(host, RegistrationsWithTransientWhoAmIHandler(host));
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
    ///     <see cref="Registrations" />, plus <c>WhoAmIHandler</c> registered as transient before <c>AddKeryx()</c> (which keeps it): each
    ///     resolution then constructs a new handler with a new <c>Identity</c>, so tests can count pipeline context constructions.
    /// </summary>
    internal static Action<IServiceCollection> RegistrationsWithTransientWhoAmIHandler(Assembly host) =>
        services =>
        {
            Registrations(host)(services);
            var handlerType = host.GetType("Sample.WhoAmIHandler", throwOnError: true)!;
            var identityType = host.GetType("Sample.Identity", throwOnError: true)!;
            services.AddTransient(handlerType, provider => Activator.CreateInstance(handlerType, provider.GetRequiredService(identityType))!);
        };

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
}
