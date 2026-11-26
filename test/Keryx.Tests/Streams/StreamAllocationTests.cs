using Keryx.Tests.Hosts;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Tests.Streams;

/// <summary>
///     The stream allocation contract (ADR 0001, Performance contract, Stream; the Phase 5 brief), measured inside a compiled Release host
///     with a handler and behaviors that complete synchronously: with no behaviors <c>Stream</c> adds nothing to what the handler
///     allocates; with behaviors it adds exactly one iterator per wrap behavior plus one per fused hook segment per <c>Stream</c> call, and
///     nothing per item. Two distinct cancellable tokens (the <c>Stream</c> token and the <c>WithCancellation</c> token) add exactly one
///     linked <c>CancellationTokenSource</c> in total, with a fused hook segment outermost and with a wrap behavior outermost over an inner
///     fused hook segment; one <c>default</c> token, or the same token twice, adds none.
/// </summary>
/// <remarks>
///     Iterator sizes are not hard-coded: each wrap behavior records the runtime type of its own iterator and of the inner stream it gets
///     from <c>next</c> (a fused segment or the next wrap behavior), and the probe measures what one instance of each type costs with
///     <c>RuntimeHelpers.GetUninitializedObject</c>. Measured like <see cref="HostAllocationTests" />: smallest of several regions, not in
///     parallel.
/// </remarks>
[Timeout(ModuleInitializer.TestTimeout)]
[NotInParallel]
public class StreamAllocationTests : HostTest
{
    [Test]
    public async Task Stream_Allocates_One_Iterator_Per_Wrap_Behavior_And_Fused_Segment_And_Nothing_Per_Item(CancellationToken cancellationToken)
    {
        var host = await CompileReleaseHostAsync(Source, cancellationToken);
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();

        var measured = Call<Dictionary<string, long[]>>(host, "Sample.Probe", "Iterators", scope.ServiceProvider);

        await Assert.That(measured.Count).IsEqualTo(14);
        foreach (var (scenario, values) in measured)
        {
            await Assert.That(values[0]).IsEqualTo(values[1]).Because($"{scenario}: measured {values[0]} B, expected {values[1]} B");
        }

        // The measurement sees the iterators at all: a fused segment, and each wrap behavior, costs something.
        await Assert.That(measured["hooks only, typed, 1 items"][1]).IsGreaterThan(measured["no behaviors, typed, 1 items"][1]);
        await Assert.That(measured["mixed, 1 items"][1]).IsGreaterThan(measured["wraps only, 1 items"][1]);
        await Assert.That(measured["wraps only, 1 items"][1]).IsGreaterThan(measured["hooks only, typed, 1 items"][1]);
    }

    [Test]
    public async Task Two_Distinct_Cancellable_Tokens_Allocate_Exactly_One_Linked_Token_Source(CancellationToken cancellationToken)
    {
        var host = await CompileReleaseHostAsync(Source, cancellationToken);
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();

        var measured = Call<Dictionary<string, long[]>>(host, "Sample.Probe", "Tokens", scope.ServiceProvider);

        await Assert.That(measured.Count).IsEqualTo(8);
        foreach (var (scenario, values) in measured)
        {
            await Assert.That(values[0]).IsEqualTo(values[1]).Because($"{scenario}: measured {values[0]} B, expected {values[1]} B");
        }

        // The linked token source is visible to the measurement.
        foreach (var outermost in (string[])["segment outermost", "wrap outermost over a segment"])
        {
            await Assert.That(measured[$"{outermost}: Stream token and WithCancellation token"][0]).IsGreaterThan(measured[$"{outermost}: Stream token only"][0]);
        }
    }

    private const string Source = """
        using System.Runtime.CompilerServices;

        [assembly: KeryxHost]

        namespace Sample;

        [BehaviorOrder(Phase.Default, Order = 10)]
        public sealed class Hook1<TRequest, TItem> : IStreamHookBehavior<TRequest, TItem> where TRequest : IStreamRequest<TItem>
        {
            public int Items;
            public ValueTask OnStartAsync(TRequest request, CancellationToken cancellationToken) => default;
            public bool OnItem(TRequest request, ref TItem item) { Items++; return true; }
            public ValueTask OnCompleteAsync(TRequest request) => default;
            public ValueTask OnErrorAsync(TRequest request, Exception exception) => default;
        }

        [BehaviorOrder(Phase.Default, Order = 20)]
        public sealed class Hook2<TRequest, TItem> : IStreamHookBehavior<TRequest, TItem> where TRequest : IStreamRequest<TItem>
        {
            public async ValueTask OnStartAsync(TRequest request, CancellationToken cancellationToken) => await default(ValueTask);
            public bool OnItem(TRequest request, ref TItem item) => true;
            public async ValueTask OnCompleteAsync(TRequest request) => await default(ValueTask);
            public ValueTask OnErrorAsync(TRequest request, Exception exception) => default;
        }

        [BehaviorOrder(Phase.Default, Order = 40)]
        public sealed class Hook3<TRequest, TItem> : IStreamHookBehavior<TRequest, TItem> where TRequest : IStreamRequest<TItem>
        {
            public ValueTask OnStartAsync(TRequest request, CancellationToken cancellationToken) => default;
            public bool OnItem(TRequest request, ref TItem item) => true;
            public ValueTask OnCompleteAsync(TRequest request) => default;
            public ValueTask OnErrorAsync(TRequest request, Exception exception) => default;
        }

        /// <summary>A wrap behavior whose own iterator, and the inner stream it gets from next, record their runtime types.</summary>
        [BehaviorOrder(Phase.Default, Order = 5)]
        public sealed class Wrap1<TRequest, TItem> : IStreamPipelineBehavior<TRequest, TItem> where TRequest : IStreamRequest<TItem>
        {
            public static Type? IteratorType;
            public static Type? InnerType;

            public IAsyncEnumerable<TItem> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, IStreamNext<TItem>
            {
                var items = Iterate(next, cancellationToken);
                IteratorType = items.GetType();
                return items;
            }

            private static async IAsyncEnumerable<TItem> Iterate<TNext>(TNext next, [EnumeratorCancellation] CancellationToken cancellationToken) where TNext : struct, IStreamNext<TItem>
            {
                var inner = next.InvokeAsync(cancellationToken);
                InnerType = inner.GetType();
                await foreach (var item in inner)
                {
                    yield return item;
                }
            }
        }

        /// <summary>Like <see cref="Wrap1{TRequest,TItem}" />, further in.</summary>
        [BehaviorOrder(Phase.Default, Order = 30)]
        public sealed class Wrap2<TRequest, TItem> : IStreamPipelineBehavior<TRequest, TItem> where TRequest : IStreamRequest<TItem>
        {
            public static Type? IteratorType;
            public static Type? InnerType;

            public IAsyncEnumerable<TItem> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, IStreamNext<TItem>
            {
                var items = Iterate(next, cancellationToken);
                IteratorType = items.GetType();
                return items;
            }

            private static async IAsyncEnumerable<TItem> Iterate<TNext>(TNext next, [EnumeratorCancellation] CancellationToken cancellationToken) where TNext : struct, IStreamNext<TItem>
            {
                var inner = next.InvokeAsync(cancellationToken);
                InnerType = inner.GetType();
                await foreach (var item in inner)
                {
                    yield return item;
                }
            }
        }

        public static class Numbers
        {
            /// <summary>Yields 1 to <paramref name="count" />, completing synchronously.</summary>
            public static async IAsyncEnumerable<int> Range(int count, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                if (count < 0)
                {
                    await Task.Yield();
                }

                for (var i = 1; i <= count; i++)
                {
                    yield return i;
                }
            }
        }

        public sealed record Plain(int Count) : IStreamRequest<int>;
        public static partial class PlainHandler
        {
            private static IAsyncEnumerable<int> Handle(Plain request, CancellationToken cancellationToken) => Numbers.Range(request.Count, cancellationToken);
        }

        public sealed record Hooks(int Count) : IStreamRequest<int>;
        public static partial class HooksHandler
        {
            [Behavior(typeof(Hook2<,>))]
            [Behavior(typeof(Hook1<,>))]
            private static IAsyncEnumerable<int> Handle(Hooks request, CancellationToken cancellationToken) => Numbers.Range(request.Count, cancellationToken);
        }

        public sealed record Wraps(int Count) : IStreamRequest<int>;
        public static partial class WrapsHandler
        {
            [Behavior(typeof(Wrap2<,>))]
            [Behavior(typeof(Wrap1<,>))]
            private static IAsyncEnumerable<int> Handle(Wraps request, CancellationToken cancellationToken) => Numbers.Range(request.Count, cancellationToken);
        }

        /// <summary>Wrap1, then Hook1 and Hook2 (one segment), then Wrap2, then Hook3 (a second segment).</summary>
        public sealed record Mixed(int Count) : IStreamRequest<int>;
        public static partial class MixedHandler
        {
            [Behavior(typeof(Hook3<,>))]
            [Behavior(typeof(Wrap2<,>))]
            [Behavior(typeof(Hook2<,>))]
            [Behavior(typeof(Hook1<,>))]
            [Behavior(typeof(Wrap1<,>))]
            private static IAsyncEnumerable<int> Handle(Mixed request, CancellationToken cancellationToken) => Numbers.Range(request.Count, cancellationToken);
        }

        /// <summary>A wrap behavior outermost, over one fused segment.</summary>
        public sealed record Outer(int Count) : IStreamRequest<int>;
        public static partial class OuterHandler
        {
            [Behavior(typeof(Hook2<,>))]
            [Behavior(typeof(Hook1<,>))]
            [Behavior(typeof(Wrap1<,>))]
            private static IAsyncEnumerable<int> Handle(Outer request, CancellationToken cancellationToken) => Numbers.Range(request.Count, cancellationToken);
        }

        public static class Probe
        {
            private const int Calls = 100;

            /// <summary>Per scenario: the bytes measured for 100 enumerations and the bytes the contract allows for them.</summary>
            public static Dictionary<string, long[]> Iterators(IServiceProvider services)
            {
                var mediator = services.GetRequiredService<KeryxMediator>();
                ISender sender = mediator;
                var results = new Dictionary<string, long[]>();
                var perCount = new List<long>();

                foreach (var count in (int[])[1, 64])
                {
                    var plain = new Plain(count);
                    var hooks = new Hooks(count);
                    var wraps = new Wraps(count);
                    var mixed = new Mixed(count);
                    var handler = Measure(() => PlainHandler.InvokeAsync(plain, default), default);

                    // No behaviors: the handler's own enumerable, nothing more.
                    results[$"no behaviors, typed, {count} items"] = [Measure(() => mediator.Stream(plain), default), handler];
                    results[$"no behaviors, generic, {count} items"] = [Measure(() => sender.Stream(plain), default), handler];

                    // Hooks only: one fused segment (the outermost element, so Stream returns it).
                    var segment = mediator.Stream(hooks).GetType();
                    results[$"hooks only, typed, {count} items"] = [Measure(() => mediator.Stream(hooks), default), handler + Calls * SizeOf(segment)];
                    results[$"hooks only, generic, {count} items"] = [Measure(() => sender.Stream(hooks), default), handler + Calls * SizeOf(segment)];

                    // Wraps only: one iterator per wrap behavior.
                    Run(mediator.Stream(wraps), default);
                    var wrapIterators = SizeOf(Wrap1<Wraps, int>.IteratorType!) + SizeOf(Wrap2<Wraps, int>.IteratorType!);
                    results[$"wraps only, {count} items"] = [Measure(() => mediator.Stream(wraps), default), handler + Calls * wrapIterators];

                    // Wrap1, segment (Hook1, Hook2), Wrap2, segment (Hook3): two wrap iterators and two segment iterators.
                    Run(mediator.Stream(mixed), default);
                    var mixedIterators = SizeOf(Wrap1<Mixed, int>.IteratorType!)
                                       + SizeOf(Wrap1<Mixed, int>.InnerType!)
                                       + SizeOf(Wrap2<Mixed, int>.IteratorType!)
                                       + SizeOf(Wrap2<Mixed, int>.InnerType!);
                    results[$"mixed, {count} items"] = [Measure(() => mediator.Stream(mixed), default), handler + Calls * mixedIterators];
                    perCount.Add(Measure(() => mediator.Stream(mixed), default));
                    perCount.Add(Measure(() => PlainHandler.InvokeAsync(plain, default), default));
                }

                // Nothing per item: 64 items cost what 1 item costs, through the pipeline and in the handler alone.
                results["mixed, per item"] = [perCount[2], perCount[0]];
                results["handler alone, per item"] = [perCount[3], perCount[1]];
                return results;
            }

            /// <summary>
            ///     Per scenario: the bytes measured for 100 enumerations with the given tokens, and the bytes with both tokens default plus, only
            ///     for two distinct cancellable tokens, one linked token source per enumeration.
            /// </summary>
            public static Dictionary<string, long[]> Tokens(IServiceProvider services)
            {
                var mediator = services.GetRequiredService<KeryxMediator>();
                using var first = new CancellationTokenSource();
                using var second = new CancellationTokenSource();
                var linked = LinkedTokenSourceSize(first.Token, second.Token);
                var results = new Dictionary<string, long[]>();

                var hooks = new Hooks(3);
                var outer = new Outer(3);
                foreach (var (name, stream) in (ValueTuple<string, Func<CancellationToken, IAsyncEnumerable<int>>>[])
                         [("segment outermost", token => mediator.Stream(hooks, token)), ("wrap outermost over a segment", token => mediator.Stream(outer, token))])
                {
                    var none = Measure(() => stream(default), default);
                    results[$"{name}: Stream token and WithCancellation token"] = [Measure(() => stream(first.Token), second.Token), none + Calls * linked];
                    results[$"{name}: Stream token only"] = [Measure(() => stream(first.Token), default), none];
                    results[$"{name}: WithCancellation token only"] = [Measure(() => stream(default), second.Token), none];
                    results[$"{name}: the same token twice"] = [Measure(() => stream(first.Token), first.Token), none];
                }

                return results;
            }

            /// <summary>What one linked token source over two tokens costs, created and disposed as a compiler-generated iterator does.</summary>
            private static long LinkedTokenSourceSize(CancellationToken first, CancellationToken second)
            {
                var smallest = long.MaxValue;
                for (var repeat = 0; repeat < 6; repeat++)
                {
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    for (var i = 0; i < Calls; i++)
                    {
                        CancellationTokenSource.CreateLinkedTokenSource(first, second).Dispose();
                    }

                    smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
                }

                return smallest / Calls;
            }

            /// <summary>What one instance of a type costs.</summary>
            private static long SizeOf(Type type)
            {
                var smallest = long.MaxValue;
                for (var repeat = 0; repeat < 6; repeat++)
                {
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    for (var i = 0; i < Calls; i++)
                    {
                        _ = RuntimeHelpers.GetUninitializedObject(type);
                    }

                    smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
                }

                return smallest / Calls;
            }

            /// <summary>The bytes allocated by 100 calls that each get a stream and enumerate it to the end (smallest of several regions).</summary>
            private static long Measure(Func<IAsyncEnumerable<int>> stream, CancellationToken enumerationToken)
            {
                Run(stream(), enumerationToken);
                var smallest = long.MaxValue;
                for (var repeat = 0; repeat < 6; repeat++)
                {
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    for (var i = 0; i < Calls; i++)
                    {
                        Run(stream(), enumerationToken);
                    }

                    smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
                }

                return smallest;
            }

            private static void Run(IAsyncEnumerable<int> stream, CancellationToken enumerationToken)
            {
                var items = stream.GetAsyncEnumerator(enumerationToken);
                while (Complete(items.MoveNextAsync()))
                {
                    _ = items.Current;
                }

                var disposed = items.DisposeAsync();
                if (!disposed.IsCompletedSuccessfully)
                {
                    throw new InvalidOperationException("The stream did not dispose synchronously.");
                }
            }

            private static bool Complete(ValueTask<bool> task) =>
                task.IsCompletedSuccessfully ? task.Result : throw new InvalidOperationException("The stream did not complete synchronously.");
        }
        """;
}
