using System.Runtime.CompilerServices;

namespace Keryx.Benchmarks.Prototype;

// The Phase 4 gate (ADR 0001, Phase 4 gate): a hand-written struct pipeline in exactly the shape the host generator emits for a request
// with behaviors, measured against a hand-written nested-call baseline that does the same work. Pipeline: a sealed pipeline context created
// lazily once per scope, holding the handler and the injected services of the behavior methods (one field per distinct service, Service0,
// Service1, ... in order of first use); one flat readonly link struct per position, holding the context and the request; each static
// behavior method called directly with explicit type arguments (no behavior instance, no interface); the last link calls the handler's
// InvokeAsync.

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

// The services the prototype behavior methods inject, one type per behavior: each counts its behavior's calls, the work the nested-call
// baseline does too. A pipeline context holds one field per distinct service (ADR 0001, Injected parameters), so distinct types keep one
// counter per behavior, as the baseline has; eight behaviors that injected one CallCounter type would share a single field.

/// <summary>The service <see cref="Behavior1" /> injects: it counts that behavior's calls.</summary>
public sealed class CallCounter1
{
    public int Calls;
}

/// <summary>The service <see cref="Behavior2" /> injects: it counts that behavior's calls.</summary>
public sealed class CallCounter2
{
    public int Calls;
}

/// <summary>The service <see cref="Behavior3" /> injects: it counts that behavior's calls.</summary>
public sealed class CallCounter3
{
    public int Calls;
}

/// <summary>The service <see cref="Behavior4" /> injects: it counts that behavior's calls.</summary>
public sealed class CallCounter4
{
    public int Calls;
}

/// <summary>The service <see cref="Behavior5" /> injects: it counts that behavior's calls.</summary>
public sealed class CallCounter5
{
    public int Calls;
}

/// <summary>The service <see cref="Behavior6" /> injects: it counts that behavior's calls.</summary>
public sealed class CallCounter6
{
    public int Calls;
}

/// <summary>The service <see cref="Behavior7" /> injects: it counts that behavior's calls.</summary>
public sealed class CallCounter7
{
    public int Calls;
}

/// <summary>The service <see cref="Behavior8" /> injects: it counts that behavior's calls.</summary>
public sealed class CallCounter8
{
    public int Calls;
}

// Eight distinct static request behaviors with open behavior methods, called with (Ping, int) in the pipeline, as an Audit behavior would be.
// Each does the same small piece of work (count the call on its injected counter) and passes the request on, synchronously.

public static class Behavior1
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CallCounter1 counter, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        counter.Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public static class Behavior2
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CallCounter2 counter, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        counter.Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public static class Behavior3
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CallCounter3 counter, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        counter.Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public static class Behavior4
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CallCounter4 counter, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        counter.Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public static class Behavior5
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CallCounter5 counter, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        counter.Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public static class Behavior6
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CallCounter6 counter, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        counter.Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public static class Behavior7
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CallCounter7 counter, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        counter.Calls++;
        return next.InvokeAsync(cancellationToken);
    }
}

public static class Behavior8
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CallCounter8 counter, CancellationToken cancellationToken)
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
        return Behavior1.Handle<Ping, int, Depth1Context.Link1>(request, new Depth1Context.Link1(context, request), context.Service0, cancellationToken);
    }

    public ValueTask<int> SendDepth3(Ping request, CancellationToken cancellationToken)
    {
        var context = Volatile.Read(ref _depth3) ?? CreateDepth3();
        return Behavior1.Handle<Ping, int, Depth3Context.Link1>(request, new Depth3Context.Link1(context, request), context.Service0, cancellationToken);
    }

    public ValueTask<int> SendDepth8(Ping request, CancellationToken cancellationToken)
    {
        var context = Volatile.Read(ref _depth8) ?? CreateDepth8();
        return Behavior1.Handle<Ping, int, Depth8Context.Link1>(request, new Depth8Context.Link1(context, request), context.Service0, cancellationToken);
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
        public readonly CallCounter1 Service0 = new();

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
        public readonly CallCounter1 Service0 = new();
        public readonly CallCounter2 Service1 = new();
        public readonly CallCounter3 Service2 = new();

        public readonly struct Link1(Depth3Context context, Ping request) : INext<int>
        {
            private readonly Depth3Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                Behavior2.Handle<Ping, int, Link2>(_request, new Link2(_context, _request), _context.Service1, cancellationToken);
        }

        public readonly struct Link2(Depth3Context context, Ping request) : INext<int>
        {
            private readonly Depth3Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                Behavior3.Handle<Ping, int, Link3>(_request, new Link3(_context, _request), _context.Service2, cancellationToken);
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
        public readonly CallCounter1 Service0 = new();
        public readonly CallCounter2 Service1 = new();
        public readonly CallCounter3 Service2 = new();
        public readonly CallCounter4 Service3 = new();
        public readonly CallCounter5 Service4 = new();
        public readonly CallCounter6 Service5 = new();
        public readonly CallCounter7 Service6 = new();
        public readonly CallCounter8 Service7 = new();

        public readonly struct Link1(Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                Behavior2.Handle<Ping, int, Link2>(_request, new Link2(_context, _request), _context.Service1, cancellationToken);
        }

        public readonly struct Link2(Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                Behavior3.Handle<Ping, int, Link3>(_request, new Link3(_context, _request), _context.Service2, cancellationToken);
        }

        public readonly struct Link3(Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                Behavior4.Handle<Ping, int, Link4>(_request, new Link4(_context, _request), _context.Service3, cancellationToken);
        }

        public readonly struct Link4(Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                Behavior5.Handle<Ping, int, Link5>(_request, new Link5(_context, _request), _context.Service4, cancellationToken);
        }

        public readonly struct Link5(Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                Behavior6.Handle<Ping, int, Link6>(_request, new Link6(_context, _request), _context.Service5, cancellationToken);
        }

        public readonly struct Link6(Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                Behavior7.Handle<Ping, int, Link7>(_request, new Link7(_context, _request), _context.Service6, cancellationToken);
        }

        public readonly struct Link7(Depth8Context context, Ping request) : INext<int>
        {
            private readonly Depth8Context _context = context;
            private readonly Ping _request = request;

            public ValueTask<int> InvokeAsync(CancellationToken cancellationToken) =>
                Behavior8.Handle<Ping, int, Link8>(_request, new Link8(_context, _request), _context.Service7, cancellationToken);
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
