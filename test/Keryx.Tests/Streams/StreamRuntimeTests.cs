using System.Reflection;
using Keryx.Tests.Hosts;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions.Enums;
using static Keryx.Tests.Streams.StreamSources;

namespace Keryx.Tests.Streams;

/// <summary>
///     Compiles a host with stream behaviors and streams through it (ADR 0001, Streaming; the Phase 5 brief): pass-through without behaviors,
///     wrap behaviors (re-yield, filter, map, calling <c>next</c> twice, not calling it), hook order, dropped items, early dispose,
///     exceptions from the handler, hooks and wrap behaviors (inside and outside a fused segment, OQ-21), cancellation through both tokens,
///     segment splitting, and typed and generic dispatch.
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
    public async Task Hooks_Start_Outer_To_Inner_And_See_Items_And_Completion_Inner_To_Outer(bool typed, CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();

        var (items, error) = await ConsumeAsync(Stream(host, scope, typed, Create(host, "Sample.Hooks", 2), cancellationToken), cancellationToken);

        await Assert.That(error).IsNull();
        await Assert.That(Text(items)).IsEqualTo("1,2");
        await Assert.That(TakeLog(host))
                    .IsEquivalentTo(
                         [
                             "HookA.start", "HookB.start", "handler>", "HookB.item 1", "HookA.item 1", "HookB.item 2", "HookA.item 2", "<handler",
                             "HookB.complete", "HookA.complete",
                         ],
                         CollectionOrdering.Matching
                     );
    }

    [Test]
    public async Task A_Hook_Drops_An_Item_Before_Outer_Hooks_And_The_Consumer_See_It(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();
        SetControl(host, dropAt: "HookB:2");

        var (items, error) = await ConsumeAsync(Sender(scope).Stream((IStreamRequest<int>)Create(host, "Sample.Hooks", 3), cancellationToken), cancellationToken);

        await Assert.That(error).IsNull();
        await Assert.That(Text(items)).IsEqualTo("1,3");
        await Assert.That(TakeLog(host))
                    .IsEquivalentTo(
                         [
                             "HookA.start", "HookB.start", "handler>", "HookB.item 1", "HookA.item 1", "HookB.item 2", "HookB.item 3", "HookA.item 3",
                             "<handler", "HookB.complete", "HookA.complete",
                         ],
                         CollectionOrdering.Matching
                     );
    }

    [Test]
    public async Task Early_Dispose_Completes_Every_Started_Hook(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();

        var (items, error) = await ConsumeAsync(Sender(scope).Stream((IStreamRequest<int>)Create(host, "Sample.Hooks", 3), cancellationToken), cancellationToken, take: 1);

        await Assert.That(error).IsNull();
        await Assert.That(Text(items)).IsEqualTo("1");
        await Assert.That(TakeLog(host))
                    .IsEquivalentTo(["HookA.start", "HookB.start", "handler>", "HookB.item 1", "HookA.item 1", "HookB.complete", "HookA.complete"], CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("handler.2", "1", new[] { "HookA.start", "HookB.start", "handler>", "HookB.item 1", "HookA.item 1", "HookB.error InvalidOperationException", "HookA.error InvalidOperationException" })]
    [Arguments("HookB.start", "", new[] { "HookA.start", "HookB.start", "HookA.error InvalidOperationException" })]
    [Arguments("HookA.start", "", new[] { "HookA.start" })]
    [Arguments("HookA.item", "", new[] { "HookA.start", "HookB.start", "handler>", "HookB.item 1", "HookA.item 1", "HookB.error InvalidOperationException", "HookA.error InvalidOperationException" })]
    public async Task Exceptions_From_The_Handler_And_From_Hooks_Reach_OnErrorAsync_Of_Started_Hooks(
        string throwAt,
        string expectedItems,
        string[] expectedLog,
        CancellationToken cancellationToken
    )
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();
        SetControl(host, throwAt);

        var (items, error) = await ConsumeAsync(Sender(scope).Stream((IStreamRequest<int>)Create(host, "Sample.Hooks", 3), cancellationToken), cancellationToken);

        await Assert.That(error).IsTypeOf<InvalidOperationException>();
        await Assert.That(error!.Message).IsEqualTo(throwAt);
        await Assert.That(Text(items)).IsEqualTo(expectedItems);
        await Assert.That(TakeLog(host)).IsEquivalentTo(expectedLog, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Terminal_Hook_Call_That_Throws_Still_Lets_The_Outer_Hooks_Complete(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();
        SetControl(host, "HookB.complete");

        var (items, error) = await ConsumeAsync(Sender(scope).Stream((IStreamRequest<int>)Create(host, "Sample.Hooks", 1), cancellationToken), cancellationToken);

        // OQ-38: the terminal calls nest like using statements; the kind (complete) was decided before HookB threw.
        await Assert.That(error).IsTypeOf<InvalidOperationException>();
        await Assert.That(error!.Message).IsEqualTo("HookB.complete");
        await Assert.That(Text(items)).IsEqualTo("1");
        await Assert.That(TakeLog(host))
                    .IsEquivalentTo(["HookA.start", "HookB.start", "handler>", "HookB.item 1", "HookA.item 1", "<handler", "HookB.complete", "HookA.complete"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task An_OnErrorAsync_That_Throws_Still_Lets_The_Outer_Hooks_Observe_The_Error(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();
        SetControl(host, "HookB.error");

        var (items, error) = await ConsumeAsync(Sender(scope).Stream((IStreamRequest<int>)Create(host, "Sample.ThrowInside", 2), cancellationToken), cancellationToken);

        // OQ-38: HookA still gets OnErrorAsync with the original error; the exception thrown last (HookB's) reaches the consumer.
        await Assert.That(error).IsTypeOf<InvalidOperationException>();
        await Assert.That(error!.Message).IsEqualTo("HookB.error");
        await Assert.That(items).IsEmpty();
        await Assert.That(TakeLog(host))
                    .IsEquivalentTo(["HookA.start", "HookB.start", "HookB.error InvalidOperationException", "HookA.error InvalidOperationException"], CollectionOrdering.Matching);
    }

    /// <summary>
    ///     OQ-38: the segment disposes the inner enumerator before any terminal hook call. After normal completion that dispose is inside the
    ///     guarded region, so its exception reaches <c>OnErrorAsync</c>; on early dispose it runs first in the terminal pass, its exception
    ///     propagates, and the hooks still complete. <c>Sample.Disposing</c>'s handler is hand-written, because a compiler-generated iterator
    ///     runs its <c>finally</c> inside the last <c>MoveNextAsync</c>, not inside <c>DisposeAsync</c>.
    /// </summary>
    [Test]
    [Arguments(int.MaxValue, "1,2", new[] { "HookA.start", "HookB.start", "HookB.item 1", "HookA.item 1", "HookB.item 2", "HookA.item 2", "handler.dispose", "HookB.error InvalidOperationException", "HookA.error InvalidOperationException" })]
    [Arguments(1, "1", new[] { "HookA.start", "HookB.start", "HookB.item 1", "HookA.item 1", "handler.dispose", "HookB.complete", "HookA.complete" })]
    public async Task An_Exception_From_Disposing_The_Inner_Enumerator_Reaches_The_Consumer_After_The_Hooks(
        int take,
        string expectedItems,
        string[] expectedLog,
        CancellationToken cancellationToken
    )
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();
        SetControl(host, "handler.dispose");

        var (items, error) = await ConsumeAsync(Sender(scope).Stream((IStreamRequest<int>)Create(host, "Sample.Disposing", 2), cancellationToken), cancellationToken, take);

        await Assert.That(error).IsTypeOf<InvalidOperationException>();
        await Assert.That(error!.Message).IsEqualTo("handler.dispose");
        await Assert.That(Text(items)).IsEqualTo(expectedItems);
        await Assert.That(TakeLog(host)).IsEquivalentTo(expectedLog, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Wrap_Behaviors_Map_Filter_Call_Next_Twice_And_Answer_Without_Next(CancellationToken cancellationToken)
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
    [Arguments("Sample.WrapOuter", new[] { "WrapFirst>", "HookA.start", "HookB.start", "handler>", "HookB.item 1", "HookA.item 1", "HookB.item 2", "HookA.item 2", "<handler", "HookB.complete", "HookA.complete", "<WrapFirst" })]
    [Arguments("Sample.WrapMiddle", new[] { "HookA.start", "WrapMid>", "HookB.start", "handler>", "HookB.item 1", "HookA.item 1", "HookB.item 2", "HookA.item 2", "<handler", "HookB.complete", "<WrapMid", "HookA.complete" })]
    [Arguments("Sample.WrapEnd", new[] { "HookA.start", "HookB.start", "WrapLast>", "handler>", "HookB.item 1", "HookA.item 1", "HookB.item 2", "HookA.item 2", "<handler", "<WrapLast", "HookB.complete", "HookA.complete" })]
    public async Task Wrap_Behaviors_Split_Hooks_Into_Segments(string request, string[] expectedLog, CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();

        var (items, error) = await ConsumeAsync(Sender(scope).Stream((IStreamRequest<int>)Create(host, request, 2), cancellationToken), cancellationToken);

        await Assert.That(error).IsNull();
        await Assert.That(Text(items)).IsEqualTo("1,2");
        await Assert.That(TakeLog(host)).IsEquivalentTo(expectedLog, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Wrap_And_Hook_Behaviors_Interleave_By_Order(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();

        var (items, error) = await ConsumeAsync(StreamTyped<int>(Mediator(host, scope), Create(host, "Sample.Mixed", 1), cancellationToken), cancellationToken);

        await Assert.That(error).IsNull();
        await Assert.That(Text(items)).IsEqualTo("1");
        await Assert.That(TakeLog(host))
                    .IsEquivalentTo(
                         [
                             "WrapFirst>", "HookA.start", "WrapMid>", "HookB.start", "HookC.start", "WrapLast>", "handler>", "HookC.item 1", "HookB.item 1",
                             "HookA.item 1", "<handler", "<WrapLast", "HookC.complete", "HookB.complete", "<WrapMid", "HookA.complete", "<WrapFirst",
                         ],
                         CollectionOrdering.Matching
                     );

        // An exception from an inner wrap behavior travels out through every segment around it, each of which observes it.
        SetControl(host, "WrapLast.item");
        var (Items, Error) = await ConsumeAsync(StreamTyped<int>(Mediator(host, scope), Create(host, "Sample.Mixed", 1), cancellationToken), cancellationToken);
        await Assert.That(Error).IsTypeOf<InvalidOperationException>();
        await Assert.That(TakeLog(host))
                    .IsEquivalentTo(
                         [
                             "WrapFirst>", "HookA.start", "WrapMid>", "HookB.start", "HookC.start", "WrapLast>", "handler>", "HookC.error InvalidOperationException",
                             "HookB.error InvalidOperationException", "HookA.error InvalidOperationException",
                         ],
                         CollectionOrdering.Matching
                     );
    }

    [Test]
    public async Task A_Non_Iterator_Wrap_Behavior_Inside_A_Segment_That_Throws_Is_Observed_By_The_Segments_Hooks(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();

        // Nothing runs at the Stream call: the outermost element is a fused segment, which is lazy.
        var stream = Sender(scope).Stream((IStreamRequest<int>)Create(host, "Sample.ThrowInside", 2), cancellationToken);
        await Assert.That(TakeLog(host)).IsEmpty();

        var (items, error) = await ConsumeAsync(stream, cancellationToken);
        await Assert.That(error).IsTypeOf<InvalidOperationException>();
        await Assert.That(error!.Message).IsEqualTo("ThrowNow");
        await Assert.That(items).IsEmpty();
        await Assert.That(TakeLog(host))
                    .IsEquivalentTo(["HookA.start", "HookB.start", "HookB.error InvalidOperationException", "HookA.error InvalidOperationException"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task An_Outermost_Non_Iterator_Wrap_Behavior_That_Throws_Throws_From_The_Stream_Call(CancellationToken cancellationToken)
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

    [Test]
    public async Task An_Exception_From_An_Outer_Wrap_Behavior_Completes_The_Inner_Segment(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();
        SetControl(host, "WrapFirst.item");

        var (items, error) = await ConsumeAsync(Sender(scope).Stream((IStreamRequest<int>)Create(host, "Sample.WrapOuter", 3), cancellationToken), cancellationToken);

        // OQ-21: the inner segment sees an early dispose, so its hooks complete; the exception still reaches the consumer.
        await Assert.That(error).IsTypeOf<InvalidOperationException>();
        await Assert.That(error!.Message).IsEqualTo("WrapFirst.item");
        await Assert.That(items).IsEmpty();
        await Assert.That(TakeLog(host))
                    .IsEquivalentTo(["WrapFirst>", "HookA.start", "HookB.start", "handler>", "HookB.item 1", "HookA.item 1", "HookB.complete", "HookA.complete"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task An_Exception_From_A_Hook_Of_An_Outer_Segment_Completes_The_Inner_Segment(CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();
        SetControl(host, "HookA.item");

        var (items, error) = await ConsumeAsync(Sender(scope).Stream((IStreamRequest<int>)Create(host, "Sample.WrapMiddle", 3), cancellationToken), cancellationToken);

        // OQ-21: HookA's segment observes its own hook's exception; HookB's segment, inside WrapMid, is disposed early and completes.
        await Assert.That(error).IsTypeOf<InvalidOperationException>();
        await Assert.That(error!.Message).IsEqualTo("HookA.item");
        await Assert.That(items).IsEmpty();
        await Assert.That(TakeLog(host))
                    .IsEquivalentTo(
                         ["HookA.start", "WrapMid>", "HookB.start", "handler>", "HookB.item 1", "HookA.item 1", "HookB.complete", "HookA.error InvalidOperationException"],
                         CollectionOrdering.Matching
                     );
    }

    /// <summary>
    ///     Cancellation through the <c>Stream</c> token, through the <c>WithCancellation</c> token, and through either when both are given
    ///     (and linked once, by the outermost element): with a fused hook segment outermost (<c>Hooks</c>) and with a wrap behavior outermost
    ///     (<c>WrapOuter</c>). The handler observes the token it is given and throws; the hooks see the exception.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(CancellationCases))]
    public async Task Cancellation_Reaches_The_Handler_Through_Both_Tokens(string request, bool streamToken, bool enumerationToken, bool cancelStreamToken, CancellationToken cancellationToken)
    {
        var (host, provider) = await StartAsync(cancellationToken);
        await using var owner = provider;
        await using var scope = provider.CreateAsyncScope();
        using var streamSource = new CancellationTokenSource();
        using var enumerationSource = new CancellationTokenSource();

        var stream = Sender(scope).Stream((IStreamRequest<int>)Create(host, request, 3), streamToken ? streamSource.Token : default);
        var (items, error) = await ConsumeAsync(
            stream,
            enumerationToken ? enumerationSource.Token : default,
            afterItem: _ => ( cancelStreamToken ? streamSource : enumerationSource ).Cancel()
        );

        await Assert.That(error).IsAssignableTo<OperationCanceledException>();
        await Assert.That(Text(items)).IsEqualTo("1");
        var log = TakeLog(host);
        await Assert.That(log).Contains("HookB.error OperationCanceledException");
        await Assert.That(log[^1]).IsEqualTo("HookA.error OperationCanceledException");
    }

    public static IEnumerable<Func<(string, bool, bool, bool)>> CancellationCases()
    {
        foreach (var request in (string[])["Sample.Hooks", "Sample.WrapOuter"])
        {
            yield return () => (request, true, false, true);
            yield return () => (request, false, true, false);
            yield return () => (request, true, true, true);
            yield return () => (request, true, true, false);
        }
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
        await Assert.That(TakeLog(host)).IsEquivalentTo(["HookA.start", "HookA.item a", "HookA.item b", "HookA.complete"], CollectionOrdering.Matching);

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
        _ = Throws(() => StreamTypedAs<int>(Mediator(host, scope), null!, cancellationToken, host.GetType("Sample.Hooks")!), typeof(ArgumentNullException));
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
        "using System.Runtime.CompilerServices;\n\n[assembly: KeryxHost]\n\nnamespace Sample;\n\n"
      + Support
      + Hook("HookA", 10)
      + Hook("HookB", 20)
      + Hook("HookC", 30)
      + Wrap("WrapFirst", 5)
      + Wrap("WrapMid", 15)
      + Wrap("WrapLast", 40)
      + """
        [BehaviorOrder(Phase.Default, Order = 1)]
        public sealed class Map : IStreamPipelineBehavior<Mapped, int>
        {
            public async IAsyncEnumerable<int> Handle<TNext>(Mapped request, TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
                where TNext : struct, IStreamNext<int>
            {
                await foreach (var item in next.InvokeAsync(cancellationToken))
                {
                    yield return item * 10;
                }
            }
        }

        [BehaviorOrder(Phase.Default, Order = 2)]
        public sealed class Filter : IStreamPipelineBehavior<Mapped, int>
        {
            public async IAsyncEnumerable<int> Handle<TNext>(Mapped request, TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
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

        public sealed class Twice : IStreamPipelineBehavior<Retried, int>
        {
            public async IAsyncEnumerable<int> Handle<TNext>(Retried request, TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
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

        public sealed class FromCache : IStreamPipelineBehavior<Cached, int>
        {
            public async IAsyncEnumerable<int> Handle<TNext>(Cached request, TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
                where TNext : struct, IStreamNext<int>
            {
                await Task.Yield();
                yield return 7;
                yield return 8;
            }
        }

        /// <summary>Not an iterator: throws when called, inside the fused segment of HookA and HookB.</summary>
        [BehaviorOrder(Phase.Default, Order = 35)]
        public sealed class ThrowNow : IStreamPipelineBehavior<ThrowInside, int>
        {
            public IAsyncEnumerable<int> Handle<TNext>(ThrowInside request, TNext next, CancellationToken cancellationToken)
                where TNext : struct, IStreamNext<int> => throw new InvalidOperationException("ThrowNow");
        }

        /// <summary>Not an iterator: throws when called, as the outermost element.</summary>
        [BehaviorOrder(Phase.Default, Order = 1)]
        public sealed class ThrowFirst : IStreamPipelineBehavior<ThrowOutside, int>
        {
            public IAsyncEnumerable<int> Handle<TNext>(ThrowOutside request, TNext next, CancellationToken cancellationToken)
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
            [Behavior(typeof(HookA<,>))]
            private static IAsyncEnumerable<int> Handle(Ticks request, CancellationToken cancellationToken) => Numbers.Range(request.Count, cancellationToken);
        }

        public sealed record Names : IStreamRequest<string>;
        public static partial class NamesHandler
        {
            [Behavior(typeof(HookA<,>))]
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

        public sealed record Disposing(int Count) : IStreamRequest<int>;
        public static partial class DisposingHandler
        {
            [Behavior(typeof(HookB<,>))]
            [Behavior(typeof(HookA<,>))]
            private static IAsyncEnumerable<int> Handle(Disposing request) => new CountThenThrowOnDispose(request.Count);
        }

        /// <summary>Yields 1 to <c>count</c>; its <c>DisposeAsync</c> logs and can throw on cue (<c>handler.dispose</c>).</summary>
        public sealed class CountThenThrowOnDispose(int count) : IAsyncEnumerable<int>
        {
            public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) => new Enumerator(count);

            private sealed class Enumerator(int count) : IAsyncEnumerator<int>
            {
                public int Current { get; private set; }

                public ValueTask<bool> MoveNextAsync()
                {
                    if (Current == count)
                    {
                        return new ValueTask<bool>(false);
                    }

                    Current++;
                    return new ValueTask<bool>(true);
                }

                public ValueTask DisposeAsync()
                {
                    Log.Write("handler.dispose");
                    Control.Throw("handler.dispose");
                    return default;
                }
            }
        }


        """
      + Request("Hooks", "HookB<,>", "HookA<,>")
      + Request("WrapOuter", "HookB<,>", "HookA<,>", "WrapFirst<,>")
      + Request("WrapMiddle", "HookB<,>", "WrapMid<,>", "HookA<,>")
      + Request("WrapEnd", "WrapLast<,>", "HookB<,>", "HookA<,>")
      + Request("Mixed", "WrapLast<,>", "HookC<,>", "HookB<,>", "WrapMid<,>", "HookA<,>", "WrapFirst<,>")
      + Request("Mapped", "Filter", "Map")
      + Request("Retried", "Twice")
      + Request("Cached", "FromCache")
      + Request("ThrowInside", "ThrowNow", "HookB<,>", "HookA<,>")
      + Request("ThrowOutside", "HookA<,>", "ThrowFirst");

    private sealed record ForeignStream : IStreamRequest<int>;
}
