using System.Runtime.CompilerServices;
using Keryx.Benchmarks.Common;
using Mediator;

namespace Keryx.Benchmarks.MediatorSingleton;

// Open behaviors applied to every message whose markers satisfy their constraint (Pass1: depth 1 and up, Pass2-3: depth 3 and up,
// Pass4-8: depth 8), listed in MediatorSetup's PipelineBehaviors.

public sealed class Pass1<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage, IDepth1
{
    public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return next(message, cancellationToken);
    }
}

public sealed class Pass2<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage, IDepth3
{
    public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return next(message, cancellationToken);
    }
}

public sealed class Pass3<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage, IDepth3
{
    public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return next(message, cancellationToken);
    }
}

public sealed class Pass4<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage, IDepth8
{
    public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return next(message, cancellationToken);
    }
}

public sealed class Pass5<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage, IDepth8
{
    public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return next(message, cancellationToken);
    }
}

public sealed class Pass6<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage, IDepth8
{
    public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return next(message, cancellationToken);
    }
}

public sealed class Pass7<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage, IDepth8
{
    public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return next(message, cancellationToken);
    }
}

public sealed class Pass8<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage, IDepth8
{
    public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return next(message, cancellationToken);
    }
}

public sealed class StreamPass1<TMessage, TResponse> : IStreamPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IStreamMessage, IStreamDepth3
{
    public async IAsyncEnumerable<TResponse> Handle(
        TMessage message,
        StreamHandlerDelegate<TMessage, TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        Probe.Hit();
        await foreach (var item in next(message, cancellationToken))
        {
            yield return item;
        }
    }
}

public sealed class StreamPass2<TMessage, TResponse> : IStreamPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IStreamMessage, IStreamDepth3
{
    public async IAsyncEnumerable<TResponse> Handle(
        TMessage message,
        StreamHandlerDelegate<TMessage, TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        Probe.Hit();
        await foreach (var item in next(message, cancellationToken))
        {
            yield return item;
        }
    }
}

public sealed class StreamPass3<TMessage, TResponse> : IStreamPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IStreamMessage, IStreamDepth3
{
    public async IAsyncEnumerable<TResponse> Handle(
        TMessage message,
        StreamHandlerDelegate<TMessage, TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        Probe.Hit();
        await foreach (var item in next(message, cancellationToken))
        {
            yield return item;
        }
    }
}
