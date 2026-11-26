using System.Runtime.CompilerServices;

namespace Keryx.Benchmarks.Prototype;

// The Phase 4 gate (ADR 0001, Phase 4 gate): a hand-written struct pipeline in exactly the shape the host generator emits for a request
// with behaviors, measured against a hand-written nested-call baseline that does the same work. Pipeline: a sealed pipeline context created
// lazily once per scope, holding the handler and the injected services of the behavior methods; one flat readonly link struct per position,
// holding the context and the request; each static behavior method called directly with explicit type arguments (no behavior instance, no
// interface); the last link calls the handler's InvokeAsync.

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

/// <summary>The service each prototype behavior method injects: it counts the calls, the work the nested-call baseline does too.</summary>
public sealed class CallCounter
{
    public int Calls;
}

// Eight distinct static request behaviors with open behavior methods, called with (Ping, int) in the pipeline, as an Audit behavior would be.
// Each does the same small piece of work (count the call on its injected counter) and passes the request on, synchronously.

public static class Behavior1
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CallCounter counter, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        counter.Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public static class Behavior2
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CallCounter counter, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        counter.Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public static class Behavior3
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CallCounter counter, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        counter.Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public static class Behavior4
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CallCounter counter, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        counter.Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public static class Behavior5
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CallCounter counter, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        counter.Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public static class Behavior6
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CallCounter counter, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        counter.Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public static class Behavior7
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CallCounter counter, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        counter.Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public static class Behavior8
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CallCounter counter, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        counter.Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

/// <summary>
///     The prototype mediator: one lazily created pipeline context per depth, as the generated <c>KeryxMediator</c> holds one per request
///     type. The injected services are constructed directly here; the generated context resolves them from the scope.
/// </summary>
public sealed class PrototypeMediator
{
    private Depth1Context? _depth1;
    private Depth3Context? _depth3;
    private Depth8Context? _depth8;

    public ValueTask<int> SendDepth1(Ping request, CancellationToken cancellationToken)
    {
        var context = Volatile.Read(ref _depth1) ?? CreateDepth1();
        return Behavior1.Handle<Ping, int, Depth1Context.Link1>(request, new Depth1Context.Link1(context, request), context.Behavior1Service0, cancellationToken);
    }

    public ValueTask<int> SendDepth3(Ping request, CancellationToken cancellationToken)
    {
        var context = Volatile.Read(ref _depth3) ?? CreateDepth3();
        return Behavior1.Handle<Ping, int, Depth3Context.Link1>(request, new Depth3Context.Link1(context, request), context.Behavior1Service0, cancellationToken);
    }

    public ValueTask<int> SendDepth8(Ping request, CancellationToken cancellationToken)
    {
        var context = Volatile.Read(ref _depth8) ?? CreateDepth8();
        return Behavior1.Handle<Ping, int, Depth8Context.Link1>(request, new Depth8Context.Link1(context, request), context.Behavior1Service0, cancellationToken);
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
        public readonly CallCounter Behavior1Service0 = new();

        public readonly struct Link1(Depth1Context context, Ping request) : INext<int>
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
        public readonly CallCounter Behavior1Service0 = new();
        public readonly CallCounter Behavior2Service0 = new();
        public readonly CallCounter Behavior3Service0 = new();

        public readonly struct Link1(Depth3Context context, Ping request) : INext<int>
        {
            private readonly Depth3Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                Behavior2.Handle<Ping, int, Link2>(_request, new Link2(_context, _request), _context.Behavior2Service0, cancellationToken);
        }

        public readonly struct Link2(Depth3Context context, Ping request) : INext<int>
        {
            private readonly Depth3Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                Behavior3.Handle<Ping, int, Link3>(_request, new Link3(_context, _request), _context.Behavior3Service0, cancellationToken);
        }

        public readonly struct Link3(Depth3Context context, Ping request) : INext<int>
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
        public readonly CallCounter Behavior1Service0 = new();
        public readonly CallCounter Behavior2Service0 = new();
        public readonly CallCounter Behavior3Service0 = new();
        public readonly CallCounter Behavior4Service0 = new();
        public readonly CallCounter Behavior5Service0 = new();
        public readonly CallCounter Behavior6Service0 = new();
        public readonly CallCounter Behavior7Service0 = new();
        public readonly CallCounter Behavior8Service0 = new();

        public readonly struct Link1(Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                Behavior2.Handle<Ping, int, Link2>(_request, new Link2(_context, _request), _context.Behavior2Service0, cancellationToken);
        }

        public readonly struct Link2(Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                Behavior3.Handle<Ping, int, Link3>(_request, new Link3(_context, _request), _context.Behavior3Service0, cancellationToken);
        }

        public readonly struct Link3(Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                Behavior4.Handle<Ping, int, Link4>(_request, new Link4(_context, _request), _context.Behavior4Service0, cancellationToken);
        }

        public readonly struct Link4(Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                Behavior5.Handle<Ping, int, Link5>(_request, new Link5(_context, _request), _context.Behavior5Service0, cancellationToken);
        }

        public readonly struct Link5(Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                Behavior6.Handle<Ping, int, Link6>(_request, new Link6(_context, _request), _context.Behavior6Service0, cancellationToken);
        }

        public readonly struct Link6(Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                Behavior7.Handle<Ping, int, Link7>(_request, new Link7(_context, _request), _context.Behavior7Service0, cancellationToken);
        }

        public readonly struct Link7(Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                Behavior8.Handle<Ping, int, Link8>(_request, new Link8(_context, _request), _context.Behavior8Service0, cancellationToken);
        }

        public readonly struct Link8(Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                _context.Handler.InvokeAsync(_request, cancellationToken);
        }
    }
}
