using System.Runtime.CompilerServices;
using Immediate.Handlers.Shared;
using Keryx.Benchmarks.Common;

namespace Keryx.Benchmarks.ImmediateHandlers;

public sealed class Pass1<TRequest, TResponse> : Behavior<TRequest, TResponse>
{
    public override ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Next(request, cancellationToken);
    }
}

public sealed class Pass2<TRequest, TResponse> : Behavior<TRequest, TResponse>
{
    public override ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Next(request, cancellationToken);
    }
}

public sealed class Pass3<TRequest, TResponse> : Behavior<TRequest, TResponse>
{
    public override ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Next(request, cancellationToken);
    }
}

public sealed class Pass4<TRequest, TResponse> : Behavior<TRequest, TResponse>
{
    public override ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Next(request, cancellationToken);
    }
}

public sealed class Pass5<TRequest, TResponse> : Behavior<TRequest, TResponse>
{
    public override ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Next(request, cancellationToken);
    }
}

public sealed class Pass6<TRequest, TResponse> : Behavior<TRequest, TResponse>
{
    public override ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Next(request, cancellationToken);
    }
}

public sealed class Pass7<TRequest, TResponse> : Behavior<TRequest, TResponse>
{
    public override ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Next(request, cancellationToken);
    }
}

public sealed class Pass8<TRequest, TResponse> : Behavior<TRequest, TResponse>
{
    public override ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Next(request, cancellationToken);
    }
}

public sealed class StreamPass1<TRequest, TResponse> : StreamingBehavior<TRequest, TResponse>
{
    public override async IAsyncEnumerable<TResponse> HandleAsync(TRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Probe.Hit();
        await foreach (var item in Next(request, cancellationToken))
        {
            yield return item;
        }
    }
}

public sealed class StreamPass2<TRequest, TResponse> : StreamingBehavior<TRequest, TResponse>
{
    public override async IAsyncEnumerable<TResponse> HandleAsync(TRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Probe.Hit();
        await foreach (var item in Next(request, cancellationToken))
        {
            yield return item;
        }
    }
}

public sealed class StreamPass3<TRequest, TResponse> : StreamingBehavior<TRequest, TResponse>
{
    public override async IAsyncEnumerable<TResponse> HandleAsync(TRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Probe.Hit();
        await foreach (var item in Next(request, cancellationToken))
        {
            yield return item;
        }
    }
}
