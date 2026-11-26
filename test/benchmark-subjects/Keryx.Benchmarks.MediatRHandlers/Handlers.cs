using Keryx.Benchmarks.Common;
using MediatR;

namespace Keryx.Benchmarks.MediatRHandlers;

public sealed class Ping0Handler : IRequestHandler<Ping0, int>
{
    public Task<int> Handle(Ping0 request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Task.FromResult(request.Value * 2);
    }
}

public sealed class Ping1Handler : IRequestHandler<Ping1, int>
{
    public Task<int> Handle(Ping1 request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Task.FromResult(request.Value * 2);
    }
}

public sealed class Ping3Handler : IRequestHandler<Ping3, int>
{
    public Task<int> Handle(Ping3 request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Task.FromResult(request.Value * 2);
    }
}

public sealed class Ping8Handler : IRequestHandler<Ping8, int>
{
    public Task<int> Handle(Ping8 request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Task.FromResult(request.Value * 2);
    }
}

public sealed class YieldPing0Handler : IRequestHandler<YieldPing0, int>
{
    public async Task<int> Handle(YieldPing0 request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Probe.Hit();
        return request.Value * 2;
    }
}

public sealed class YieldPing1Handler : IRequestHandler<YieldPing1, int>
{
    public async Task<int> Handle(YieldPing1 request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Probe.Hit();
        return request.Value * 2;
    }
}

public sealed class YieldPing3Handler : IRequestHandler<YieldPing3, int>
{
    public async Task<int> Handle(YieldPing3 request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Probe.Hit();
        return request.Value * 2;
    }
}

public sealed class YieldPing8Handler : IRequestHandler<YieldPing8, int>
{
    public async Task<int> Handle(YieldPing8 request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Probe.Hit();
        return request.Value * 2;
    }
}

public sealed class Count0Handler : IStreamRequestHandler<Count0, int>
{
    public IAsyncEnumerable<int> Handle(Count0 request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Numbers.Range(request.Count, cancellationToken);
    }
}

public sealed class Count3Handler : IStreamRequestHandler<Count3, int>
{
    public IAsyncEnumerable<int> Handle(Count3 request, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Numbers.Range(request.Count, cancellationToken);
    }
}

public sealed class Pinged1Handler : INotificationHandler<Pinged1>
{
    public Task Handle(Pinged1 notification, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Task.CompletedTask;
    }
}

public sealed class Pinged5HandlerA : INotificationHandler<Pinged5>
{
    public Task Handle(Pinged5 notification, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Task.CompletedTask;
    }
}

public sealed class Pinged5HandlerB : INotificationHandler<Pinged5>
{
    public Task Handle(Pinged5 notification, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Task.CompletedTask;
    }
}

public sealed class Pinged5HandlerC : INotificationHandler<Pinged5>
{
    public Task Handle(Pinged5 notification, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Task.CompletedTask;
    }
}

public sealed class Pinged5HandlerD : INotificationHandler<Pinged5>
{
    public Task Handle(Pinged5 notification, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Task.CompletedTask;
    }
}

public sealed class Pinged5HandlerE : INotificationHandler<Pinged5>
{
    public Task Handle(Pinged5 notification, CancellationToken cancellationToken)
    {
        Probe.Hit();
        return Task.CompletedTask;
    }
}
