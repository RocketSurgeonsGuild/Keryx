using Keryx.Benchmarks.Common;

namespace Keryx.Benchmarks.Baseline;

// The hand-written direct-call baseline (ADR 0001, Benchmarks): the same requests and handler bodies as every library, called
// directly. Behaviors are not modelled: at every depth the baseline is the handler call alone, the floor every mediator is measured against.

public sealed record Ping0(int Value);
public sealed record Ping1(int Value);
public sealed record Ping3(int Value);
public sealed record Ping8(int Value);
public sealed record YieldPing0(int Value);
public sealed record YieldPing1(int Value);
public sealed record YieldPing3(int Value);
public sealed record YieldPing8(int Value);
public sealed record YieldUnitValueTask(int Value);
public sealed record YieldUnitTask(int Value);
public sealed record Count0(int Count);
public sealed record Count3(int Count);
public sealed record Pinged1(int Value);
public sealed record Pinged5(int Value);

public static class Handlers
{
    public static ValueTask<int> Handle(Ping0 request)
    {
        Probe.Hit();
        return new(request.Value * 2);
    }

    public static ValueTask<int> Handle(Ping1 request)
    {
        Probe.Hit();
        return new(request.Value * 2);
    }

    public static ValueTask<int> Handle(Ping3 request)
    {
        Probe.Hit();
        return new(request.Value * 2);
    }

    public static ValueTask<int> Handle(Ping8 request)
    {
        Probe.Hit();
        return new(request.Value * 2);
    }

    public static async ValueTask<int> Handle(YieldPing0 request)
    {
        await Task.Yield();
        Probe.Hit();
        return request.Value * 2;
    }

    public static async ValueTask<int> Handle(YieldPing1 request)
    {
        await Task.Yield();
        Probe.Hit();
        return request.Value * 2;
    }

    public static async ValueTask<int> Handle(YieldPing3 request)
    {
        await Task.Yield();
        Probe.Hit();
        return request.Value * 2;
    }

    public static async ValueTask<int> Handle(YieldPing8 request)
    {
        await Task.Yield();
        Probe.Hit();
        return request.Value * 2;
    }

    public static async ValueTask Handle(YieldUnitValueTask request)
    {
        await Task.Yield();
        Probe.Hit();
    }

    public static async Task Handle(YieldUnitTask request)
    {
        await Task.Yield();
        Probe.Hit();
    }

    public static IAsyncEnumerable<int> Handle(Count0 request)
    {
        Probe.Hit();
        return Numbers.Range(request.Count);
    }

    public static IAsyncEnumerable<int> Handle(Count3 request)
    {
        Probe.Hit();
        return Numbers.Range(request.Count);
    }

    public static ValueTask HandleA(Pinged1 notification)
    {
        Probe.Hit();
        return default;
    }

    /// <summary>Calls the one handler of <see cref="Pinged1" />. Not async: the floor has no state machine of its own.</summary>
    public static ValueTask Publish(Pinged1 notification) => HandleA(notification);

    public static ValueTask HandleA(Pinged5 notification)
    {
        Probe.Hit();
        return default;
    }

    public static ValueTask HandleB(Pinged5 notification)
    {
        Probe.Hit();
        return default;
    }

    public static ValueTask HandleC(Pinged5 notification)
    {
        Probe.Hit();
        return default;
    }

    public static ValueTask HandleD(Pinged5 notification)
    {
        Probe.Hit();
        return default;
    }

    public static ValueTask HandleE(Pinged5 notification)
    {
        Probe.Hit();
        return default;
    }

    /// <summary>
    ///     Calls each handler of <see cref="Pinged5" /> in turn, as a sequential publish does. Not async: while the handlers complete
    ///     synchronously it runs no state machine, so it stays the floor; <see cref="PublishSlow" /> takes over from the first one that does not.
    /// </summary>
    public static ValueTask Publish(Pinged5 notification)
    {
        var a = HandleA(notification);
        if (!a.IsCompletedSuccessfully)
        {
            return PublishSlow(a, notification, 1);
        }

        a.GetAwaiter().GetResult();
        var b = HandleB(notification);
        if (!b.IsCompletedSuccessfully)
        {
            return PublishSlow(b, notification, 2);
        }

        b.GetAwaiter().GetResult();
        var c = HandleC(notification);
        if (!c.IsCompletedSuccessfully)
        {
            return PublishSlow(c, notification, 3);
        }

        c.GetAwaiter().GetResult();
        var d = HandleD(notification);
        if (!d.IsCompletedSuccessfully)
        {
            return PublishSlow(d, notification, 4);
        }

        d.GetAwaiter().GetResult();
        var e = HandleE(notification);
        if (!e.IsCompletedSuccessfully)
        {
            return PublishSlow(e, notification, 5);
        }

        e.GetAwaiter().GetResult();
        return default;
    }

    /// <summary>Awaits the pending handler, then runs the handlers after it (<paramref name="next" /> is the 0-based index of the next one).</summary>
    private static async ValueTask PublishSlow(ValueTask pending, Pinged5 notification, int next)
    {
        await pending;
        if (next <= 1)
        {
            await HandleB(notification);
        }

        if (next <= 2)
        {
            await HandleC(notification);
        }

        if (next <= 3)
        {
            await HandleD(notification);
        }

        if (next <= 4)
        {
            await HandleE(notification);
        }
    }
}
