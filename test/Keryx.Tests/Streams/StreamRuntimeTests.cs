using System.Reflection;
using Keryx.Tests.Hosts;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions.Enums;
using static Keryx.Tests.Streams.StreamSources;

namespace Keryx.Tests.Streams;

/// <summary>
///     Compiles a host with static stream behaviors and streams through it (ADR 0001, Streaming): pass-through without behaviors, stream
///     behaviors that re-yield (outer to inner), filter, map, call <c>next</c> twice or not at all, early dispose, exceptions from the handler
///     and from stream behaviors, a synchronous throw from a non-iterator stream behavior (outermost: at the <c>Stream</c> call), cancellation
///     through both tokens with a stream behavior outermost, and typed and generic dispatch.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class StreamRuntimeTests : HostTest
{
    private static readonly string Source = BuildSource();

    [Test]
    public async Task Without_Behaviors_Stream_Returns_The_Handlers_Enumerable_As_Is(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();
        var shared = host.GetType("Sample.Shared", throwOnError: true)!.GetField("Items")!.GetValue(null);

        await Assert.That(StreamTyped<int>(Mediator(host, scope), Create(host, "Sample.Plain"), cancellationToken)).IsSameReferenceAs(shared);
        await Assert.That(Sender(scope).Stream((IStreamRequest<int>)Create(host, "Sample.Plain"), cancellationToken)).IsSameReferenceAs(shared);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Stream_Behaviors_Run_Outer_To_Inner_And_Re_Yield_The_Items(bool typed, CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();

        var (items, error) = await ConsumeAsync(Stream(host, scope, typed, Create(host, "Sample.Wraps", 2), cancellationToken), cancellationToken);

        await Assert.That(error).IsNull();
        await Assert.That(Text(items)).IsEqualTo("1,2");
        await Assert.That(TakeLog(host)).IsEquivalentTo(["WrapA>", "WrapB>", "handler>", "<handler", "<WrapB", "<WrapA"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Early_Dispose_Ends_Every_Stream_Behavior_Inner_To_Outer(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();

        var (items, error) = await ConsumeAsync(Sender(scope).Stream((IStreamRequest<int>)Create(host, "Sample.Wraps", 3), cancellationToken), cancellationToken, take: 1);

        await Assert.That(error).IsNull();
        await Assert.That(Text(items)).IsEqualTo("1");
        await Assert.That(TakeLog(host)).IsEquivalentTo(["WrapA>", "WrapB>", "handler>", "WrapB.abort", "WrapA.abort"], CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("handler.2", "1")]
    [Arguments("WrapB.item", "")]
    [Arguments("WrapA.item", "")]
    public async Task Exceptions_From_The_Handler_And_From_Stream_Behaviors_Reach_The_Consumer(string throwAt, string expectedItems, CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();
        SetControl(host, throwAt);

        var (items, error) = await ConsumeAsync(Sender(scope).Stream((IStreamRequest<int>)Create(host, "Sample.Wraps", 3), cancellationToken), cancellationToken);

        await Assert.That(error).IsTypeOf<InvalidOperationException>();
        await Assert.That(error!.Message).IsEqualTo(throwAt);
        await Assert.That(Text(items)).IsEqualTo(expectedItems);
        await Assert.That(TakeLog(host)).IsEquivalentTo(["WrapA>", "WrapB>", "handler>", "WrapB.abort", "WrapA.abort"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Stream_Behaviors_Map_Filter_Call_Next_Twice_And_Answer_Without_Next(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();
        var sender = Sender(scope);

        // Map (outer) multiplies what Filter (inner) lets through: the even items.
        var (Items, Error) = await ConsumeAsync(sender.Stream((IStreamRequest<int>)Create(host, "Sample.Mapped", 4), cancellationToken), cancellationToken);
        await Assert.That(Text(Items)).IsEqualTo("20,40");
        await Assert.That(TakeLog(host)).IsEquivalentTo(["handler>", "<handler"], CollectionOrdering.Matching);

        // Twice gets the rest of the pipeline twice from its link.
        var retried = await ConsumeAsync(sender.Stream((IStreamRequest<int>)Create(host, "Sample.Retried", 2), cancellationToken), cancellationToken);
        await Assert.That(Text(retried.Items)).IsEqualTo("1,2,1,2");
        await Assert.That(TakeLog(host)).IsEquivalentTo(["handler>", "<handler", "handler>", "<handler"], CollectionOrdering.Matching);

        // FromCache never calls next, so the handler never runs.
        var cached = await ConsumeAsync(StreamTyped<int>(Mediator(host, scope), Create(host, "Sample.Cached", 2), cancellationToken), cancellationToken);
        await Assert.That(Text(cached.Items)).IsEqualTo("7,8");
        await Assert.That(TakeLog(host)).IsEmpty();
    }

    [Test]
    public async Task An_Inner_Non_Iterator_Stream_Behavior_That_Throws_Ends_The_Outer_Ones(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();

        // ThrowNow runs when WrapA gets its inner stream, on the first MoveNextAsync, not at the Stream call.
        var stream = Sender(scope).Stream((IStreamRequest<int>)Create(host, "Sample.ThrowInside", 2), cancellationToken);
        var (items, error) = await ConsumeAsync(stream, cancellationToken);

        await Assert.That(error).IsTypeOf<InvalidOperationException>();
        await Assert.That(error!.Message).IsEqualTo("ThrowNow");
        await Assert.That(items).IsEmpty();
        await Assert.That(TakeLog(host)).IsEquivalentTo(["WrapA>", "WrapA.abort"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task An_Outermost_Non_Iterator_Stream_Behavior_That_Throws_Throws_From_The_Stream_Call(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();
        var request = Create(host, "Sample.ThrowOutside", 2);

        var generic = Throws(() => Sender(scope).Stream((IStreamRequest<int>)request, cancellationToken));
        var typed = Throws(() => StreamTyped<int>(Mediator(host, scope), request, cancellationToken));

        await Assert.That(generic.Message).IsEqualTo("ThrowFirst");
        await Assert.That(typed.Message).IsEqualTo("ThrowFirst");
        await Assert.That(TakeLog(host)).IsEmpty();
    }

    /// <summary>
    ///     Cancellation through the <c>Stream</c> token, through the <c>WithCancellation</c> token, and through either when both are given
    ///     (and linked once, by the outermost stream behavior's iterator). The handler observes the token it is given and throws; the stream
    ///     behaviors end with the exception.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(CancellationCases))]
    public async Task Cancellation_Reaches_The_Handler_Through_Both_Tokens(bool streamToken, bool enumerationToken, bool cancelStreamToken, CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();
        using var streamSource = new CancellationTokenSource();
        using var enumerationSource = new CancellationTokenSource();

        var stream = Sender(scope).Stream((IStreamRequest<int>)Create(host, "Sample.Wraps", 3), streamToken ? streamSource.Token : default);
        var (items, error) = await ConsumeAsync(
            stream,
            enumerationToken ? enumerationSource.Token : default,
            afterItem: _ => ( cancelStreamToken ? streamSource : enumerationSource ).Cancel()
        );

        await Assert.That(error).IsAssignableTo<OperationCanceledException>();
        await Assert.That(Text(items)).IsEqualTo("1");
        await Assert.That(TakeLog(host)).IsEquivalentTo(["WrapA>", "WrapB>", "handler>", "WrapB.abort", "WrapA.abort"], CollectionOrdering.Matching);
    }

    public static IEnumerable<Func<(bool, bool, bool)>> CancellationCases()
    {
        yield return () => (true, false, true);
        yield return () => (false, true, false);
        yield return () => (true, true, true);
        yield return () => (true, true, false);
    }

    [Test]
    public async Task Struct_And_Reference_Item_Stream_Requests_Dispatch_On_Both_Entry_Points(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();
        var mediator = Mediator(host, scope);

        var ticks = Create(host, "Sample.Ticks", 2);
        await Assert.That(Text(( await ConsumeAsync(StreamTyped<int>(mediator, ticks, cancellationToken), cancellationToken) ).Items)).IsEqualTo("1,2");
        await Assert.That(Text(( await ConsumeAsync(Sender(scope).Stream((IStreamRequest<int>)ticks, cancellationToken), cancellationToken) ).Items)).IsEqualTo("1,2");
        _ = TakeLog(host);

        var (Items, Error) = await ConsumeAsync(Sender(scope).Stream((IStreamRequest<string>)Create(host, "Sample.Names"), cancellationToken), cancellationToken);
        await Assert.That(Items).IsEquivalentTo(["a", "b"], CollectionOrdering.Matching);
        await Assert.That(TakeLog(host)).IsEquivalentTo(["WrapA>", "<WrapA"], CollectionOrdering.Matching);

        // The Shape overload, called with a Square (statically typed as a Shape), still dispatches on the runtime type (OQ-31).
        var shapeOverload = mediator.GetType().GetMethod("Stream", [host.GetType("Sample.Shape")!, typeof(CancellationToken)])!;
        var square = (IAsyncEnumerable<int>)shapeOverload.Invoke(mediator, [Create(host, "Sample.Square", 5), cancellationToken])!;
        await Assert.That(Text(( await ConsumeAsync(square, cancellationToken) ).Items)).IsEqualTo("100");
    }

    [Test]
    public async Task Null_And_Unknown_Stream_Requests_Throw_At_The_Stream_Call(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();
        var sender = Sender(scope);

        _ = Throws(() => sender.Stream<int>(null!, cancellationToken), typeof(ArgumentNullException));
        _ = Throws(() => StreamTypedAs<int>(Mediator(host, scope), null!, cancellationToken, host.GetType("Sample.Wraps")!), typeof(ArgumentNullException));
        var unknown = (KeryxHandlerNotFoundException)Throws(() => sender.Stream(new ForeignStream(), cancellationToken), typeof(KeryxHandlerNotFoundException));
        await Assert.That(unknown.RequestType).IsEqualTo(typeof(ForeignStream));
        await Assert.That(TakeLog(host)).IsEmpty();
    }

    private static string Text(List<int> items) => string.Join(",", items.Select(static i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static Exception Throws(Func<object?> call, Type? expected = null)
    {
        try
        {
            _ = call();
        }
#pragma warning disable CA1031 // The test asserts on the exception.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return expected is null || expected.IsInstanceOfType(exception)
                ? exception
                : throw new InvalidOperationException($"Expected {expected.Name}, got {exception.GetType().Name}.", exception);
        }

        throw new InvalidOperationException("Expected the Stream call itself to throw.");
    }

    private static IAsyncEnumerable<T> StreamTypedAs<T>(object mediator, object? request, CancellationToken cancellationToken, Type requestType)
    {
        var overload = mediator.GetType().GetMethod("Stream", BindingFlags.Public | BindingFlags.Instance, [requestType, typeof(CancellationToken)])!;
        try
        {
            return (IAsyncEnumerable<T>)overload.Invoke(mediator, [request, cancellationToken])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is { } inner)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }

    private static IAsyncEnumerable<int> Stream(Assembly host, AsyncServiceScope scope, bool typed, object request, CancellationToken cancellationToken) =>
        typed ? StreamTyped<int>(Mediator(host, scope), request, cancellationToken) : Sender(scope).Stream((IStreamRequest<int>)request, cancellationToken);

    private async Task<(Assembly Host, ServiceProvider Provider)> StartAsync(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        _ = TakeLog(host);
        return (host, BuildProvider(host));
    }

    private static object Mediator(Assembly host, AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService(host.GetType("Sample.KeryxMediator", throwOnError: true)!);

    private static ISender Sender(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<ISender>();

    private static string BuildSource() =>
        "using System.Runtime.CompilerServices;\n\nnamespace Sample;\n\n"
      + Support
      + Wrap("WrapA", 10)
      + Wrap("WrapB", 20)
      + """
        [StreamBehavior]
        [BehaviorOrder(Phase.Default, Order = 1)]
        public static class Map
        {
            public static async IAsyncEnumerable<int> Handle<TNext>(Mapped request, TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
                where TNext : struct, IStreamNext<int>
            {
                await foreach (var item in next.InvokeAsync(cancellationToken))
                {
                    yield return item * 10;
                }
            }
        }

        [StreamBehavior]
        [BehaviorOrder(Phase.Default, Order = 2)]
        public static class Filter
        {
            public static async IAsyncEnumerable<int> Handle<TNext>(Mapped request, TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
                where TNext : struct, IStreamNext<int>
            {
                await foreach (var item in next.InvokeAsync(cancellationToken))
                {
                    if (item % 2 == 0)
                    {
                        yield return item;
                    }
                }
            }
        }

        [StreamBehavior]
        public static class Twice
        {
            public static async IAsyncEnumerable<int> Handle<TNext>(Retried request, TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
                where TNext : struct, IStreamNext<int>
            {
                await foreach (var item in next.InvokeAsync(cancellationToken))
                {
                    yield return item;
                }

                await foreach (var item in next.InvokeAsync(cancellationToken))
                {
                    yield return item;
                }
            }
        }

        [StreamBehavior]
        public static class FromCache
        {
            public static async IAsyncEnumerable<int> Handle<TNext>(Cached request, TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
                where TNext : struct, IStreamNext<int>
            {
                await Task.Yield();
                yield return 7;
                yield return 8;
            }
        }

        /// <summary>Not an iterator: throws when called, inside WrapA.</summary>
        [StreamBehavior]
        [BehaviorOrder(Phase.Default, Order = 35)]
        public static class ThrowNow
        {
            public static IAsyncEnumerable<int> Handle<TNext>(ThrowInside request, TNext next, CancellationToken cancellationToken)
                where TNext : struct, IStreamNext<int> => throw new InvalidOperationException("ThrowNow");
        }

        /// <summary>Not an iterator: throws when called, as the outermost element.</summary>
        [StreamBehavior]
        [BehaviorOrder(Phase.Default, Order = 1)]
        public static class ThrowFirst
        {
            public static IAsyncEnumerable<int> Handle<TNext>(ThrowOutside request, TNext next, CancellationToken cancellationToken)
                where TNext : struct, IStreamNext<int> => throw new InvalidOperationException("ThrowFirst");
        }

        public static class Shared
        {
            public static readonly IAsyncEnumerable<int> Items = Numbers.Range(2, default);
        }

        public sealed record Plain : IStreamRequest<int>;
        public static partial class PlainHandler
        {
            private static IAsyncEnumerable<int> Handle(Plain request) => Shared.Items;
        }

        public readonly record struct Ticks(int Count) : IStreamRequest<int>;
        public static partial class TicksHandler
        {
            [Behavior(typeof(WrapA))]
            private static IAsyncEnumerable<int> Handle(Ticks request, CancellationToken cancellationToken) => Numbers.Range(request.Count, cancellationToken);
        }

        public sealed record Names : IStreamRequest<string>;
        public static partial class NamesHandler
        {
            [Behavior(typeof(WrapA))]
            private static async IAsyncEnumerable<string> Handle(Names request, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await Task.Yield();
                yield return "a";
                yield return "b";
            }
        }

        public record Shape(int Count) : IStreamRequest<int>;
        public static partial class ShapeHandler
        {
            private static IAsyncEnumerable<int> Handle(Shape request, CancellationToken cancellationToken) => Numbers.Range(request.Count, cancellationToken);
        }

        public sealed record Square(int Count) : Shape(Count);
        public static partial class SquareHandler
        {
            private static async IAsyncEnumerable<int> Handle(Square request)
            {
                await Task.Yield();
                yield return 100;
            }
        }


        """
      + Request("Wraps", "WrapB", "WrapA")
      + Request("Mapped", "Filter", "Map")
      + Request("Retried", "Twice")
      + Request("Cached", "FromCache")
      + Request("ThrowInside", "ThrowNow", "WrapA")
      + Request("ThrowOutside", "WrapA", "ThrowFirst");

    private sealed record ForeignStream : IStreamRequest<int>;
}
