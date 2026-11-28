using Immediate.Handlers.Shared;
using Keryx.Benchmarks.Common;

namespace Keryx.Benchmarks.ImmediateHandlers;

// Behaviors are attached per handler class ([Behaviors]), so each request gets exactly its depth. Immediate.Handlers has no
// mediator: a caller resolves the generated Handler class and calls it, which is the only (typed) entry point.

[Handler]
public static partial class Ping0Handler
{
    private static ValueTask<int> HandleAsync(Ping0 request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return new(request.Value * 2);
    }
}

[Handler]
[Behaviors(typeof(Pass1<,>))]
public static partial class Ping1Handler
{
    private static ValueTask<int> HandleAsync(Ping1 request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return new(request.Value * 2);
    }
}

[Handler]
[Behaviors(typeof(Pass1<,>), typeof(Pass2<,>), typeof(Pass3<,>))]
public static partial class Ping3Handler
{
    private static ValueTask<int> HandleAsync(Ping3 request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return new(request.Value * 2);
    }
}

[Handler]
[Behaviors(typeof(Pass1<,>), typeof(Pass2<,>), typeof(Pass3<,>), typeof(Pass4<,>), typeof(Pass5<,>), typeof(Pass6<,>), typeof(Pass7<,>), typeof(Pass8<,>))]
public static partial class Ping8Handler
{
    private static ValueTask<int> HandleAsync(Ping8 request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return new(request.Value * 2);
    }
}

[Handler]
public static partial class YieldPing0Handler
{
    private static async ValueTask<int> HandleAsync(YieldPing0 request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Probe.Hit();
        return request.Value * 2;
    }
}

[Handler]
[Behaviors(typeof(Pass1<,>))]
public static partial class YieldPing1Handler
{
    private static async ValueTask<int> HandleAsync(YieldPing1 request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Probe.Hit();
        return request.Value * 2;
    }
}

[Handler]
[Behaviors(typeof(Pass1<,>), typeof(Pass2<,>), typeof(Pass3<,>))]
public static partial class YieldPing3Handler
{
    private static async ValueTask<int> HandleAsync(YieldPing3 request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Probe.Hit();
        return request.Value * 2;
    }
}

[Handler]
[Behaviors(typeof(Pass1<,>), typeof(Pass2<,>), typeof(Pass3<,>), typeof(Pass4<,>), typeof(Pass5<,>), typeof(Pass6<,>), typeof(Pass7<,>), typeof(Pass8<,>))]
public static partial class YieldPing8Handler
{
    private static async ValueTask<int> HandleAsync(YieldPing8 request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Probe.Hit();
        return request.Value * 2;
    }
}

[Handler]
public static partial class Count0Handler
{
    private static IAsyncEnumerable<int> HandleAsync(Count0 request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Numbers.Range(request.Count, cancellationToken);
    }
}

[Handler]
[Behaviors(typeof(StreamPass1<,>), typeof(StreamPass2<,>), typeof(StreamPass3<,>))]
public static partial class Count3Handler
{
    private static IAsyncEnumerable<int> HandleAsync(Count3 request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Numbers.Range(request.Count, cancellationToken);
    }
}
