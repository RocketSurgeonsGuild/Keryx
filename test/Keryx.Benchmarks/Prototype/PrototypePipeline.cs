using System.Runtime.CompilerServices;

namespace Keryx.Benchmarks.Prototype;

// The Phase 4 gate (ADR 0001, Phase 4 gate): a hand-written struct pipeline in exactly the shape the host generator emits for a request
// with behaviors, measured against a hand-written nested-call baseline that does the same work. Pipeline: a sealed pipeline context created
// lazily once per scope, holding the closed behavior instances; one flat readonly link struct per position, holding the context and the
// request; each behavior's Handle<TNext> called on its concrete closed type; the last link calls the handler's static InvokeAsync.

/// <summary>The request every prototype pipeline sends.</summary>
public sealed record Ping(int Value) : IRequest<int>;

/// <summary>
///     The handler half the handler generator emits for a handler with injected parameters: an instance invoker, the instance held by the
///     pipeline context.
/// </summary>
public sealed class PingHandler
{
    public ValueTask<int> InvokeAsync(Ping request, CancellationToken cancellationToken) => new(request.Value * 2);
}

// Eight distinct open behaviors, closed over (Ping, int) in the pipeline, as AuditBehavior<TRequest, TResponse> would be. Each does the
// same small piece of work (count the call) and passes the request on, synchronously.

public sealed class Behavior1<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public int Calls;

    public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse>
    {
        Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public sealed class Behavior2<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public int Calls;

    public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse>
    {
        Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public sealed class Behavior3<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public int Calls;

    public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse>
    {
        Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public sealed class Behavior4<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public int Calls;

    public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse>
    {
        Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public sealed class Behavior5<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public int Calls;

    public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse>
    {
        Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public sealed class Behavior6<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public int Calls;

    public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse>
    {
        Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public sealed class Behavior7<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public int Calls;

    public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse>
    {
        Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public sealed class Behavior8<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public int Calls;

    public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse>
    {
        Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

/// <summary>
///     The prototype mediator: one lazily created pipeline context per depth, as the generated <c>KeryxMediator</c> holds one per request
///     type. Behaviors are constructed directly here; the generated context resolves them from the scope.
/// </summary>
public sealed class PrototypeMediator
{
    private Depth1Context? _depth1;
    private Depth3Context? _depth3;
    private Depth8Context? _depth8;

    public ValueTask<int> SendDepth1(Ping request, CancellationToken cancellationToken)
    {
        var context = Volatile.Read(ref _depth1) ?? CreateDepth1();
        return context.Behavior1.Handle(request, new Depth1Context.Link1(context, request), cancellationToken);
    }

    public ValueTask<int> SendDepth3(Ping request, CancellationToken cancellationToken)
    {
        var context = Volatile.Read(ref _depth3) ?? CreateDepth3();
        return context.Behavior1.Handle(request, new Depth3Context.Link1(context, request), cancellationToken);
    }

    public ValueTask<int> SendDepth8(Ping request, CancellationToken cancellationToken)
    {
        var context = Volatile.Read(ref _depth8) ?? CreateDepth8();
        return context.Behavior1.Handle(request, new Depth8Context.Link1(context, request), cancellationToken);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Depth1Context CreateDepth1() => Interlocked.CompareExchange(ref _depth1, new(), null) ?? _depth1!;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Depth3Context CreateDepth3() => Interlocked.CompareExchange(ref _depth3, new(), null) ?? _depth3!;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Depth8Context CreateDepth8() => Interlocked.CompareExchange(ref _depth8, new(), null) ?? _depth8!;

    private sealed class Depth1Context
    {
        public readonly PingHandler Handler = new();
        public readonly Behavior1<Ping, int> Behavior1 = new();

        public readonly struct Link1(PrototypeMediator.Depth1Context context, Ping request) : INext<int>
        {
            private readonly Depth1Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                _context.Handler.InvokeAsync(_request, cancellationToken);
        }
    }

    private sealed class Depth3Context
    {
        public readonly PingHandler Handler = new();
        public readonly Behavior1<Ping, int> Behavior1 = new();
        public readonly Behavior2<Ping, int> Behavior2 = new();
        public readonly Behavior3<Ping, int> Behavior3 = new();

        public readonly struct Link1(PrototypeMediator.Depth3Context context, Ping request) : INext<int>
        {
            private readonly Depth3Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                _context.Behavior2.Handle(_request, new Link2(_context, _request), cancellationToken);
        }

        public readonly struct Link2(PrototypeMediator.Depth3Context context, Ping request) : INext<int>
        {
            private readonly Depth3Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                _context.Behavior3.Handle(_request, new Link3(_context, _request), cancellationToken);
        }

        public readonly struct Link3(PrototypeMediator.Depth3Context context, Ping request) : INext<int>
        {
            private readonly Depth3Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                _context.Handler.InvokeAsync(_request, cancellationToken);
        }
    }

    private sealed class Depth8Context
    {
        public readonly PingHandler Handler = new();
        public readonly Behavior1<Ping, int> Behavior1 = new();
        public readonly Behavior2<Ping, int> Behavior2 = new();
        public readonly Behavior3<Ping, int> Behavior3 = new();
        public readonly Behavior4<Ping, int> Behavior4 = new();
        public readonly Behavior5<Ping, int> Behavior5 = new();
        public readonly Behavior6<Ping, int> Behavior6 = new();
        public readonly Behavior7<Ping, int> Behavior7 = new();
        public readonly Behavior8<Ping, int> Behavior8 = new();

        public readonly struct Link1(PrototypeMediator.Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                _context.Behavior2.Handle(_request, new Link2(_context, _request), cancellationToken);
        }

        public readonly struct Link2(PrototypeMediator.Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                _context.Behavior3.Handle(_request, new Link3(_context, _request), cancellationToken);
        }

        public readonly struct Link3(PrototypeMediator.Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                _context.Behavior4.Handle(_request, new Link4(_context, _request), cancellationToken);
        }

        public readonly struct Link4(PrototypeMediator.Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                _context.Behavior5.Handle(_request, new Link5(_context, _request), cancellationToken);
        }

        public readonly struct Link5(PrototypeMediator.Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                _context.Behavior6.Handle(_request, new Link6(_context, _request), cancellationToken);
        }

        public readonly struct Link6(PrototypeMediator.Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                _context.Behavior7.Handle(_request, new Link7(_context, _request), cancellationToken);
        }

        public readonly struct Link7(PrototypeMediator.Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                _context.Behavior8.Handle(_request, new Link8(_context, _request), cancellationToken);
        }

        public readonly struct Link8(PrototypeMediator.Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                _context.Handler.InvokeAsync(_request, cancellationToken);
        }
    }
}
