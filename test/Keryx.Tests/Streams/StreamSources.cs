using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Keryx.Tests.Streams;

/// <summary>
///     Building blocks for stream test hosts: a log, a switchboard that makes stream behaviors and handlers throw on cue, static stream
///     behaviors with open behavior methods that log what they see, and helpers that consume a stream on the test side.
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
            /// <summary>The point that throws an <c>InvalidOperationException</c>, such as <c>WrapA.item</c> or <c>handler.2</c>.</summary>
            public static string? ThrowAt;

            public static void Set(string? throwAt) { ThrowAt = throwAt; }

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
    ///     A static stream behavior named <paramref name="name" /> at <c>Phase.Default</c>, <c>Order = order</c>, with an open behavior method:
    ///     an <c>async</c> iterator that logs when it starts, re-yields the inner stream (and can throw on each item on cue), and logs when it
    ///     ends: <c>&lt;NAME</c> after the inner stream completed, <c>NAME.abort</c> when it ended any other way (an exception, cancellation or
    ///     early dispose). <paramref name="constraints" /> follows the message constraint (<c>, IAudited</c> or <c> where TItem : class</c>).
    /// </summary>
    public static string Wrap(string name, int order, string attributes = "", string constraints = "") =>
        """
        [StreamBehavior]
        ATTRIBUTES
        [BehaviorOrder(Phase.Default, Order = ORDER)]
        public static class NAME
        {
            public static async IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest request, TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
                where TRequest : IStreamRequest<TItem>CONSTRAINTS
                where TNext : struct, IStreamNext<TItem>
            {
                Log.Write("NAME>");
                var completed = false;
                try
                {
                    await foreach (var item in next.InvokeAsync(cancellationToken))
                    {
                        Control.Throw("NAME.item");
                        yield return item;
                    }

                    completed = true;
                }
                finally
                {
                    Log.Write(completed ? "<NAME" : "NAME.abort");
                }
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

    public static void SetControl(Assembly host, string? throwAt = null) =>
        _ = host.GetType("Sample.Control", throwOnError: true)!.GetMethod("Set")!.Invoke(null, [throwAt]);

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
