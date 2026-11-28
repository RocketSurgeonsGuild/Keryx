using System.Runtime.CompilerServices;
using Keryx.Benchmarks.Common;

namespace Keryx.Benchmarks.KeryxHost;

// Pass-through static behaviors: each records the call (only in --verify runs) and hands the request on synchronously. The generated
// pipeline calls each behavior method directly, with explicit type arguments.

/// <summary>A pass-through request behavior: records the call and hands the request on synchronously.</summary>
[RequestBehavior]
public static class Pass1
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        Probe.Hit();
        return next.InvokeAsync(cancellationToken);
    }
}

/// <summary>A pass-through request behavior: records the call and hands the request on synchronously.</summary>
[RequestBehavior]
public static class Pass2
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        Probe.Hit();
        return next.InvokeAsync(cancellationToken);
    }
}

/// <summary>A pass-through request behavior: records the call and hands the request on synchronously.</summary>
[RequestBehavior]
public static class Pass3
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        Probe.Hit();
        return next.InvokeAsync(cancellationToken);
    }
}

/// <summary>A pass-through request behavior: records the call and hands the request on synchronously.</summary>
[RequestBehavior]
public static class Pass4
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        Probe.Hit();
        return next.InvokeAsync(cancellationToken);
    }
}

/// <summary>A pass-through request behavior: records the call and hands the request on synchronously.</summary>
[RequestBehavior]
public static class Pass5
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        Probe.Hit();
        return next.InvokeAsync(cancellationToken);
    }
}

/// <summary>A pass-through request behavior: records the call and hands the request on synchronously.</summary>
[RequestBehavior]
public static class Pass6
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        Probe.Hit();
        return next.InvokeAsync(cancellationToken);
    }
}

/// <summary>A pass-through request behavior: records the call and hands the request on synchronously.</summary>
[RequestBehavior]
public static class Pass7
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        Probe.Hit();
        return next.InvokeAsync(cancellationToken);
    }
}

/// <summary>A pass-through request behavior: records the call and hands the request on synchronously.</summary>
[RequestBehavior]
public static class Pass8
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        Probe.Hit();
        return next.InvokeAsync(cancellationToken);
    }
}

/// <summary>A stream behavior: re-yields the inner stream from its own iterator.</summary>
[StreamBehavior]
public static class StreamPass1
{
    public static async IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(
        TRequest request,
        TNext next,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
        where TRequest : IStreamRequest<TItem>
        where TNext : struct, IStreamNext<TItem>
    {
        Probe.Hit();
        await foreach (var item in next.InvokeAsync(cancellationToken))
        {
            yield return item;
        }
    }
}

/// <summary>A stream behavior: re-yields the inner stream from its own iterator.</summary>
[StreamBehavior]
public static class StreamPass2
{
    public static async IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(
        TRequest request,
        TNext next,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
        where TRequest : IStreamRequest<TItem>
        where TNext : struct, IStreamNext<TItem>
    {
        Probe.Hit();
        await foreach (var item in next.InvokeAsync(cancellationToken))
        {
            yield return item;
        }
    }
}

/// <summary>A stream behavior: re-yields the inner stream from its own iterator.</summary>
[StreamBehavior]
public static class StreamPass3
{
    public static async IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(
        TRequest request,
        TNext next,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
        where TRequest : IStreamRequest<TItem>
        where TNext : struct, IStreamNext<TItem>
    {
        Probe.Hit();
        await foreach (var item in next.InvokeAsync(cancellationToken))
        {
            yield return item;
        }
    }
}
