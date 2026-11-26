using System.Runtime.CompilerServices;
using Keryx.Benchmarks.Common;

namespace Keryx.Benchmarks.KeryxHost;

// Pass-through behaviors: each records the call (only in --verify runs) and hands the request on synchronously.

public sealed class Pass1<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext<TResponse>
    {
        Probe.Hit();
        return next.InvokeAsync(cancellationToken);
    }
}

public sealed class Pass2<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext<TResponse>
    {
        Probe.Hit();
        return next.InvokeAsync(cancellationToken);
    }
}

public sealed class Pass3<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext<TResponse>
    {
        Probe.Hit();
        return next.InvokeAsync(cancellationToken);
    }
}

public sealed class Pass4<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext<TResponse>
    {
        Probe.Hit();
        return next.InvokeAsync(cancellationToken);
    }
}

public sealed class Pass5<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext<TResponse>
    {
        Probe.Hit();
        return next.InvokeAsync(cancellationToken);
    }
}

public sealed class Pass6<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext<TResponse>
    {
        Probe.Hit();
        return next.InvokeAsync(cancellationToken);
    }
}

public sealed class Pass7<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext<TResponse>
    {
        Probe.Hit();
        return next.InvokeAsync(cancellationToken);
    }
}

public sealed class Pass8<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext<TResponse>
    {
        Probe.Hit();
        return next.InvokeAsync(cancellationToken);
    }
}

/// <summary>A wrap stream behavior: re-yields the inner stream from its own iterator.</summary>
public sealed class StreamPass1<TRequest, TItem> : IStreamPipelineBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    public async IAsyncEnumerable<TItem> Handle<TNext>(TRequest request, TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamNext<TItem>
    {
        Probe.Hit();
        await foreach (var item in next.InvokeAsync(cancellationToken))
        {
            yield return item;
        }
    }
}

/// <summary>A wrap stream behavior: re-yields the inner stream from its own iterator.</summary>
public sealed class StreamPass2<TRequest, TItem> : IStreamPipelineBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    public async IAsyncEnumerable<TItem> Handle<TNext>(TRequest request, TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamNext<TItem>
    {
        Probe.Hit();
        await foreach (var item in next.InvokeAsync(cancellationToken))
        {
            yield return item;
        }
    }
}

/// <summary>A wrap stream behavior: re-yields the inner stream from its own iterator.</summary>
public sealed class StreamPass3<TRequest, TItem> : IStreamPipelineBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    public async IAsyncEnumerable<TItem> Handle<TNext>(TRequest request, TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamNext<TItem>
    {
        Probe.Hit();
        await foreach (var item in next.InvokeAsync(cancellationToken))
        {
            yield return item;
        }
    }
}

/// <summary>A hook stream behavior: adjacent hooks are fused into one generated iterator.</summary>
public sealed class StreamHook1<TRequest, TItem> : IStreamHookBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    public ValueTask OnStartAsync(TRequest request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return default;
    }

    public bool OnItem(TRequest request, ref TItem item) => true;

    public ValueTask OnCompleteAsync(TRequest request) => default;

    public ValueTask OnErrorAsync(TRequest request, Exception exception) => default;
}

/// <summary>A hook stream behavior: adjacent hooks are fused into one generated iterator.</summary>
public sealed class StreamHook2<TRequest, TItem> : IStreamHookBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    public ValueTask OnStartAsync(TRequest request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return default;
    }

    public bool OnItem(TRequest request, ref TItem item) => true;

    public ValueTask OnCompleteAsync(TRequest request) => default;

    public ValueTask OnErrorAsync(TRequest request, Exception exception) => default;
}

/// <summary>A hook stream behavior: adjacent hooks are fused into one generated iterator.</summary>
public sealed class StreamHook3<TRequest, TItem> : IStreamHookBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    public ValueTask OnStartAsync(TRequest request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return default;
    }

    public bool OnItem(TRequest request, ref TItem item) => true;

    public ValueTask OnCompleteAsync(TRequest request) => default;

    public ValueTask OnErrorAsync(TRequest request, Exception exception) => default;
}
