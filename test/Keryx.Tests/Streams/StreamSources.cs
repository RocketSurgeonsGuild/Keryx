using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Keryx.Tests.Streams;

/// <summary>
///     Building blocks for stream test hosts: a log, a switchboard that makes hooks, wrap behaviors and handlers throw or drop items on cue,
///     open generic hook and wrap behaviors that log what they see, and helpers that consume a stream on the test side.
/// </summary>
internal static class StreamSources
{
    /// <summary><c>Log</c> (what ran, in order) and <c>Control</c> (where to throw, which item to drop), as host source.</summary>
    public const string Support = """
        public static class Log
        {
            public static readonly List<string> Entries = [];
            public static void Write(string entry) { lock (Entries) { Entries.Add(entry); } }
            public static string[] Take() { lock (Entries) { var entries = Entries.ToArray(); Entries.Clear(); return entries; } }
        }

        public static class Control
        {
            /// <summary>The point that throws an <c>InvalidOperationException</c>, such as <c>HookA.start</c> or <c>handler.2</c>.</summary>
            public static string? ThrowAt;

            /// <summary>A hook and an item it drops, such as <c>HookB:2</c>.</summary>
            public static string? DropAt;

            public static void Set(string? throwAt, string? dropAt) { ThrowAt = throwAt; DropAt = dropAt; }

            public static void Throw(string point)
            {
                if (ThrowAt == point)
                {
                    throw new InvalidOperationException(point);
                }
            }
        }

        /// <summary>Yields 1 to <paramref name="count" />, checking the token before each item.</summary>
        public static class Numbers
        {
            public static async IAsyncEnumerable<int> Range(int count, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                Log.Write("handler>");
                for (var i = 1; i <= count; i++)
                {
                    await Task.Yield();
                    cancellationToken.ThrowIfCancellationRequested();
                    Control.Throw("handler." + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    yield return i;
                }

                Log.Write("<handler");
            }
        }


        """;

    /// <summary>
    ///     An open generic hook behavior named <paramref name="name" /> at <c>Phase.Default</c>, <c>Order = order</c>, that logs every hook
    ///     call, can throw from any hook call and drop an item on cue, and yields once in <c>OnStartAsync</c>.
    /// </summary>
    public static string Hook(string name, int order, string attributes = "", string constraints = "") =>
        """
        ATTRIBUTES
        [BehaviorOrder(Phase.Default, Order = ORDER)]
        public sealed class NAME<TRequest, TItem> : IStreamHookBehavior<TRequest, TItem> where TRequest : IStreamRequest<TItem>CONSTRAINTS
        {
            public async ValueTask OnStartAsync(TRequest request, CancellationToken cancellationToken)
            {
                Log.Write("NAME.start");
                await Task.Yield();
                Control.Throw("NAME.start");
            }

            public bool OnItem(TRequest request, ref TItem item)
            {
                var text = item?.ToString();
                Log.Write("NAME.item " + text);
                Control.Throw("NAME.item");
                return Control.DropAt != "NAME:" + text;
            }

            public ValueTask OnCompleteAsync(TRequest request)
            {
                Log.Write("NAME.complete");
                Control.Throw("NAME.complete");
                return default;
            }

            public ValueTask OnErrorAsync(TRequest request, Exception exception)
            {
                Log.Write("NAME.error " + exception.GetType().Name);
                Control.Throw("NAME.error");
                return default;
            }
        }


        """.Replace("ATTRIBUTES", attributes, StringComparison.Ordinal)
           .Replace("ORDER", order.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
           .Replace("CONSTRAINTS", constraints, StringComparison.Ordinal)
           .Replace("NAME", name, StringComparison.Ordinal);

    /// <summary>
    ///     An open generic wrap behavior named <paramref name="name" /> at <c>Phase.Default</c>, <c>Order = order</c>: an <c>async</c> iterator
    ///     that logs when it starts and ends and re-yields the inner stream, and can throw on each item on cue.
    /// </summary>
    public static string Wrap(string name, int order, string attributes = "", string constraints = "") =>
        """
        ATTRIBUTES
        [BehaviorOrder(Phase.Default, Order = ORDER)]
        public sealed class NAME<TRequest, TItem> : IStreamPipelineBehavior<TRequest, TItem> where TRequest : IStreamRequest<TItem>CONSTRAINTS
        {
            public async IAsyncEnumerable<TItem> Handle<TNext>(TRequest request, TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
                where TNext : struct, IStreamNext<TItem>
            {
                Log.Write("NAME>");
                await foreach (var item in next.InvokeAsync(cancellationToken))
                {
                    Control.Throw("NAME.item");
                    yield return item;
                }

                Log.Write("<NAME");
            }
        }


        """.Replace("ATTRIBUTES", attributes, StringComparison.Ordinal)
           .Replace("ORDER", order.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
           .Replace("CONSTRAINTS", constraints, StringComparison.Ordinal)
           .Replace("NAME", name, StringComparison.Ordinal);

    /// <summary>A stream request <c>name(int Count)</c> over <c>int</c> whose static handler yields 1 to <c>Count</c>, with behaviors attached.</summary>
    public static string Request(string name, params string[] behaviors)
    {
        var attributes = string.Concat(behaviors.Select(static b => $"    [Behavior(typeof({b}))]\n"));
        return $$"""
                 public sealed record {{name}}(int Count) : IStreamRequest<int>;
                 public static partial class {{name}}Handler
                 {
                 {{attributes}}    private static IAsyncEnumerable<int> Handle({{name}} request, CancellationToken cancellationToken) => Numbers.Range(request.Count, cancellationToken);
                 }


                 """;
    }

    public static string[] TakeLog(Assembly host) =>
        (string[])host.GetType("Sample.Log", throwOnError: true)!.GetMethod("Take", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;

    public static void SetControl(Assembly host, string? throwAt = null, string? dropAt = null) =>
        _ = host.GetType("Sample.Control", throwOnError: true)!.GetMethod("Set")!.Invoke(null, [throwAt, dropAt]);

    /// <summary>
    ///     Enumerates a stream to the end (or until <paramref name="take" /> items), with <paramref name="enumerationToken" /> as the
    ///     <c>WithCancellation</c> token, calling <paramref name="afterItem" /> after each item. Returns the items and the exception, if any.
    /// </summary>
    public static async Task<(List<T> Items, Exception? Error)> ConsumeAsync<T>(
        IAsyncEnumerable<T> stream,
        CancellationToken enumerationToken = default,
        int take = int.MaxValue,
        Action<int>? afterItem = null
    )
    {
        var items = new List<T>();
        try
        {
            await foreach (var item in stream.WithCancellation(enumerationToken))
            {
                items.Add(item);
                afterItem?.Invoke(items.Count);
                if (items.Count == take)
                {
                    break;
                }
            }
        }
#pragma warning disable CA1031 // The tests assert on the exception a stream ends with, whatever its type.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return (items, exception);
        }

        return (items, null);
    }

    /// <summary>Calls the host mediator's typed <c>Stream</c> overload for the request's type.</summary>
    public static IAsyncEnumerable<T> StreamTyped<T>(object mediator, object request, CancellationToken cancellationToken)
    {
        var overload = mediator.GetType().GetMethod("Stream", BindingFlags.Public | BindingFlags.Instance, [request.GetType(), typeof(CancellationToken)])!;
        try
        {
            return (IAsyncEnumerable<T>)overload.Invoke(mediator, [request, cancellationToken])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is { } inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }
}
