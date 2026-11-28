using Keryx.Benchmarks.Common;

namespace Keryx.Benchmarks.KeryxHost;

// Behaviors are attached per handler ([Behavior] on the method), so each request gets exactly its depth.

public static partial class Ping0Handler
{
    private static ValueTask<int> Handle(Ping0 request)
    {
        Probe.Hit();
        return new(request.Value * 2);
    }
}

public static partial class Ping1Handler
{
    [Behavior(typeof(Pass1))]
    private static ValueTask<int> Handle(Ping1 request)
    {
        Probe.Hit();
        return new(request.Value * 2);
    }
}

public static partial class Ping3Handler
{
    [Behavior(typeof(Pass1))]
    [Behavior(typeof(Pass2))]
    [Behavior(typeof(Pass3))]
    private static ValueTask<int> Handle(Ping3 request)
    {
        Probe.Hit();
        return new(request.Value * 2);
    }
}

public static partial class Ping8Handler
{
    [Behavior(typeof(Pass1))]
    [Behavior(typeof(Pass2))]
    [Behavior(typeof(Pass3))]
    [Behavior(typeof(Pass4))]
    [Behavior(typeof(Pass5))]
    [Behavior(typeof(Pass6))]
    [Behavior(typeof(Pass7))]
    [Behavior(typeof(Pass8))]
    private static ValueTask<int> Handle(Ping8 request)
    {
        Probe.Hit();
        return new(request.Value * 2);
    }
}

public static partial class YieldPing0Handler
{
    private static async ValueTask<int> Handle(YieldPing0 request)
    {
        await Task.Yield();
        Probe.Hit();
        return request.Value * 2;
    }
}

public static partial class YieldPing1Handler
{
    [Behavior(typeof(Pass1))]
    private static async ValueTask<int> Handle(YieldPing1 request)
    {
        await Task.Yield();
        Probe.Hit();
        return request.Value * 2;
    }
}

public static partial class YieldPing3Handler
{
    [Behavior(typeof(Pass1))]
    [Behavior(typeof(Pass2))]
    [Behavior(typeof(Pass3))]
    private static async ValueTask<int> Handle(YieldPing3 request)
    {
        await Task.Yield();
        Probe.Hit();
        return request.Value * 2;
    }
}

public static partial class YieldPing8Handler
{
    [Behavior(typeof(Pass1))]
    [Behavior(typeof(Pass2))]
    [Behavior(typeof(Pass3))]
    [Behavior(typeof(Pass4))]
    [Behavior(typeof(Pass5))]
    [Behavior(typeof(Pass6))]
    [Behavior(typeof(Pass7))]
    [Behavior(typeof(Pass8))]
    private static async ValueTask<int> Handle(YieldPing8 request)
    {
        await Task.Yield();
        Probe.Hit();
        return request.Value * 2;
    }
}

public static partial class Count0Handler
{
    private static IAsyncEnumerable<int> Handle(Count0 request)
    {
        Probe.Hit();
        return Numbers.Range(request.Count);
    }
}

public static partial class Count3Handler
{
    [Behavior(typeof(StreamPass1))]
    [Behavior(typeof(StreamPass2))]
    [Behavior(typeof(StreamPass3))]
    private static IAsyncEnumerable<int> Handle(Count3 request)
    {
        Probe.Hit();
        return Numbers.Range(request.Count);
    }
}

public static partial class Pinged1Handler
{
    private static ValueTask Handle(Pinged1 notification)
    {
        Probe.Hit();
        return default;
    }
}

public static partial class Pinged5HandlerA
{
    private static ValueTask Handle(Pinged5 notification)
    {
        Probe.Hit();
        return default;
    }
}

public static partial class Pinged5HandlerB
{
    private static ValueTask Handle(Pinged5 notification)
    {
        Probe.Hit();
        return default;
    }
}

public static partial class Pinged5HandlerC
{
    private static ValueTask Handle(Pinged5 notification)
    {
        Probe.Hit();
        return default;
    }
}

public static partial class Pinged5HandlerD
{
    private static ValueTask Handle(Pinged5 notification)
    {
        Probe.Hit();
        return default;
    }
}

public static partial class Pinged5HandlerE
{
    private static ValueTask Handle(Pinged5 notification)
    {
        Probe.Hit();
        return default;
    }
}

// Handlers of IRequest that return non-generic ValueTask / Task and yield, so the generated invoker adapts them to ValueTask<Unit> through
// UnitAdapter.ToUnit's async path.

public static partial class YieldUnitValueTaskHandler
{
    private static async ValueTask Handle(YieldUnitValueTask request)
    {
        await Task.Yield();
        Probe.Hit();
    }
}

public static partial class YieldUnitTaskHandler
{
    private static async Task Handle(YieldUnitTask request)
    {
        await Task.Yield();
        Probe.Hit();
    }
}
