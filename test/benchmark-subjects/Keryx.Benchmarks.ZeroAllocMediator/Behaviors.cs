using Keryx.Benchmarks.Common;
using ZeroAlloc.Mediator;

namespace Keryx.Benchmarks.ZeroAllocMediator;

// ZeroAlloc.Mediator behaviors apply to every request, or to the one type AppliesTo names, so each request with behaviors gets its
// own pass-through classes. Orders are unique across the assembly (ZAM006); a lower order runs further out. ZeroAlloc.Mediator has
// no stream behaviors.

[PipelineBehavior(Order = 1, AppliesTo = typeof(Ping1))]
public sealed class Ping1Pass1 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 2, AppliesTo = typeof(Ping3))]
public sealed class Ping3Pass1 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 3, AppliesTo = typeof(Ping3))]
public sealed class Ping3Pass2 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 4, AppliesTo = typeof(Ping3))]
public sealed class Ping3Pass3 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 5, AppliesTo = typeof(Ping8))]
public sealed class Ping8Pass1 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 6, AppliesTo = typeof(Ping8))]
public sealed class Ping8Pass2 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 7, AppliesTo = typeof(Ping8))]
public sealed class Ping8Pass3 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 8, AppliesTo = typeof(Ping8))]
public sealed class Ping8Pass4 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 9, AppliesTo = typeof(Ping8))]
public sealed class Ping8Pass5 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 10, AppliesTo = typeof(Ping8))]
public sealed class Ping8Pass6 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 11, AppliesTo = typeof(Ping8))]
public sealed class Ping8Pass7 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 12, AppliesTo = typeof(Ping8))]
public sealed class Ping8Pass8 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 13, AppliesTo = typeof(YieldPing1))]
public sealed class YieldPing1Pass1 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 14, AppliesTo = typeof(YieldPing3))]
public sealed class YieldPing3Pass1 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 15, AppliesTo = typeof(YieldPing3))]
public sealed class YieldPing3Pass2 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 16, AppliesTo = typeof(YieldPing3))]
public sealed class YieldPing3Pass3 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 17, AppliesTo = typeof(YieldPing8))]
public sealed class YieldPing8Pass1 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 18, AppliesTo = typeof(YieldPing8))]
public sealed class YieldPing8Pass2 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 19, AppliesTo = typeof(YieldPing8))]
public sealed class YieldPing8Pass3 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 20, AppliesTo = typeof(YieldPing8))]
public sealed class YieldPing8Pass4 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 21, AppliesTo = typeof(YieldPing8))]
public sealed class YieldPing8Pass5 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 22, AppliesTo = typeof(YieldPing8))]
public sealed class YieldPing8Pass6 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 23, AppliesTo = typeof(YieldPing8))]
public sealed class YieldPing8Pass7 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}

[PipelineBehavior(Order = 24, AppliesTo = typeof(YieldPing8))]
public sealed class YieldPing8Pass8 : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next
    )
    {
        Probe.Hit();
        return next(request, state, ct);
    }
}
