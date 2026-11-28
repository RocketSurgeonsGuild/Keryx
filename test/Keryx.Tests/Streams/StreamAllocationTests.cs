using Keryx.Tests.Hosts;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Tests.Streams;

/// <summary>
///     The stream allocation contract (ADR 0001, Performance contract, Stream; the Phase 5 brief), measured inside a compiled Release host
///     with a handler and behaviors that complete synchronously: with no behaviors <c>Stream</c> adds nothing to what the handler
///     allocates; with static stream behaviors it adds exactly one iterator per stream behavior per <c>Stream</c> call, and nothing per item.
///     Two distinct cancellable tokens (the <c>Stream</c> token and the <c>WithCancellation</c> token) add exactly one linked
///     <c>CancellationTokenSource</c> in total, with a stream behavior outermost (alone, or over another one); one <c>default</c> token, or the
///     same token twice, adds none.
/// </summary>
/// <remarks>
///     Iterator sizes are not hard-coded: each stream behavior records the runtime type of its own iterator per request type, and the probe
///     measures what one instance of each type costs with
///     <c>RuntimeHelpers.GetUninitializedObject</c>. Measured like <see cref="HostAllocationTests" />: smallest of several regions, not in
///     parallel.
/// </remarks>
[Timeout(ModuleInitializer.TestTimeout)]
[NotInParallel]
public class StreamAllocationTests : HostTest
{
    [Test]
    public async Task Stream_Allocates_One_Iterator_Per_Stream_Behavior_And_Nothing_Per_Item(CancellationToken cancellationToken)
    {
        var host = await CompileReleaseHostAsync(Source, cancellationToken);
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();

        var measured = Call<Dictionary<string, long[]>>(host, "Sample.Probe", "Iterators", scope.ServiceProvider);

        await Assert.That(measured.Count).IsEqualTo(12);
        foreach (var (scenario, values) in measured)
        {
            await Assert.That(values[0]).IsEqualTo(values[1]).Because($"{scenario}: measured {values[0]} B, expected {values[1]} B");
        }

        // The measurement sees the iterators at all: each stream behavior costs something.
        await Assert.That(measured["one stream behavior, 1 items"][1]).IsGreaterThan(measured["no behaviors, typed, 1 items"][1]);
        await Assert.That(measured["two stream behaviors, typed, 1 items"][1]).IsGreaterThan(measured["one stream behavior, 1 items"][1]);
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
        foreach (var outermost in (string[])["one stream behavior", "a stream behavior outermost over another"])
        {
            await Assert.That(measured[$"{outermost}: Stream token and WithCancellation token"][0]).IsGreaterThan(measured[$"{outermost}: Stream token only"][0]);
        }
    }

    private const string Source = """
        using System.Runtime.CompilerServices;


        namespace Sample;

        /// <summary>The runtime types of the stream behaviors' own iterators, per request type.</summary>
        public static class Recorded<TRequest>
        {
            public static Type? Wrap1;
            public static Type? Wrap2;
        }

        /// <summary>A stream behavior whose own iterator records its runtime type.</summary>
        [StreamBehavior]
        [BehaviorOrder(Phase.Default, Order = 5)]
        public static class Wrap1
        {
            public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IStreamRequest<TItem>
                where TNext : struct, IStreamNext<TItem>
            {
                var items = Iterate<TItem, TNext>(next, cancellationToken);
                Recorded<TRequest>.Wrap1 = items.GetType();
                return items;
            }

            private static async IAsyncEnumerable<TItem> Iterate<TItem, TNext>(TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
                where TNext : struct, IStreamNext<TItem>
            {
                await foreach (var item in next.InvokeAsync(cancellationToken))
                {
                    yield return item;
                }
            }
        }

        /// <summary>Like <see cref="Wrap1" />, further in, and an async iterator of its own.</summary>
        [StreamBehavior]
        [BehaviorOrder(Phase.Default, Order = 30)]
        public static class Wrap2
        {
            public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IStreamRequest<TItem>
                where TNext : struct, IStreamNext<TItem>
            {
                var items = Iterate<TItem, TNext>(next, cancellationToken);
                Recorded<TRequest>.Wrap2 = items.GetType();
                return items;
            }

            private static async IAsyncEnumerable<TItem> Iterate<TItem, TNext>(TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
                where TNext : struct, IStreamNext<TItem>
            {
                await foreach (var item in next.InvokeAsync(cancellationToken))
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

        public sealed record Solo(int Count) : IStreamRequest<int>;
        public static partial class SoloHandler
        {
            [Behavior(typeof(Wrap1))]
            private static IAsyncEnumerable<int> Handle(Solo request, CancellationToken cancellationToken) => Numbers.Range(request.Count, cancellationToken);
        }

        public sealed record Wraps(int Count) : IStreamRequest<int>;
        public static partial class WrapsHandler
        {
            [Behavior(typeof(Wrap2))]
            [Behavior(typeof(Wrap1))]
            private static IAsyncEnumerable<int> Handle(Wraps request, CancellationToken cancellationToken) => Numbers.Range(request.Count, cancellationToken);
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
                    var single = new Solo(count);
                    var wraps = new Wraps(count);
                    var handler = Measure(() => PlainHandler.InvokeAsync(plain, default), default);

                    // No behaviors: the handler's own enumerable, nothing more.
                    results[$"no behaviors, typed, {count} items"] = [Measure(() => mediator.Stream(plain), default), handler];
                    results[$"no behaviors, generic, {count} items"] = [Measure(() => sender.Stream(plain), default), handler];

                    // One stream behavior: its iterator.
                    Run(mediator.Stream(single), default);
                    results[$"one stream behavior, {count} items"] = [Measure(() => mediator.Stream(single), default), handler + Calls * SizeOf(Recorded<Solo>.Wrap1!)];

                    // Two stream behaviors: one iterator each.
                    Run(mediator.Stream(wraps), default);
                    var iterators = SizeOf(Recorded<Wraps>.Wrap1!) + SizeOf(Recorded<Wraps>.Wrap2!);
                    results[$"two stream behaviors, typed, {count} items"] = [Measure(() => mediator.Stream(wraps), default), handler + Calls * iterators];
                    results[$"two stream behaviors, generic, {count} items"] = [Measure(() => sender.Stream(wraps), default), handler + Calls * iterators];
                    perCount.Add(Measure(() => mediator.Stream(wraps), default));
                    perCount.Add(Measure(() => PlainHandler.InvokeAsync(plain, default), default));
                }

                // Nothing per item: 64 items cost what 1 item costs, through the pipeline and in the handler alone.
                results["two stream behaviors, per item"] = [perCount[2], perCount[0]];
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

                var single = new Solo(3);
                var wraps = new Wraps(3);
                foreach (var (name, stream) in (ValueTuple<string, Func<CancellationToken, IAsyncEnumerable<int>>>[])
                         [("one stream behavior", token => mediator.Stream(single, token)), ("a stream behavior outermost over another", token => mediator.Stream(wraps, token))])
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
