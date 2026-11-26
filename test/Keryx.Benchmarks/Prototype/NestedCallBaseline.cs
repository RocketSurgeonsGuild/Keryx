namespace Keryx.Benchmarks.Prototype;

// The hand-written nested-call baselines of the Phase 4 gate: the same work as the struct pipeline (count the call, pass the request on,
// call the handler), written as plain direct calls between sealed, non-generic classes. One independent chain per depth, each behavior
// holding the next one (or the handler) in a readonly field, so every call site has one fixed target and no depth check: no interfaces,
// no generics, no link structs. The cost floor a generated pipeline is compared against.

/// <summary>The depth-1 nested-call baseline: 1 behavior, then the handler.</summary>
public sealed class NestedCallDepth1
{
    private readonly Behavior1 _first = new(new PingHandler());

    public ValueTask<int> Send(Ping request, CancellationToken cancellationToken) => _first.Handle(request, cancellationToken);

    private sealed class Behavior1(PingHandler next)
    {
        private readonly PingHandler _next = next;
        public int Calls;

        public ValueTask<int> Handle(Ping request, CancellationToken cancellationToken)
        {
            Calls++;
            return _next.InvokeAsync(request, cancellationToken);
        }
    }
}

/// <summary>The depth-3 nested-call baseline: 3 behaviors, then the handler.</summary>
public sealed class NestedCallDepth3
{
    private readonly Behavior1 _first = new(new(new(new PingHandler())));

    public ValueTask<int> Send(Ping request, CancellationToken cancellationToken) => _first.Handle(request, cancellationToken);

    private sealed class Behavior1(Behavior2 next)
    {
        private readonly Behavior2 _next = next;
        public int Calls;

        public ValueTask<int> Handle(Ping request, CancellationToken cancellationToken)
        {
            Calls++;
            return _next.Handle(request, cancellationToken);
        }
    }

    private sealed class Behavior2(Behavior3 next)
    {
        private readonly Behavior3 _next = next;
        public int Calls;

        public ValueTask<int> Handle(Ping request, CancellationToken cancellationToken)
        {
            Calls++;
            return _next.Handle(request, cancellationToken);
        }
    }

    private sealed class Behavior3(PingHandler next)
    {
        private readonly PingHandler _next = next;
        public int Calls;

        public ValueTask<int> Handle(Ping request, CancellationToken cancellationToken)
        {
            Calls++;
            return _next.InvokeAsync(request, cancellationToken);
        }
    }
}

/// <summary>The depth-8 nested-call baseline: 8 behaviors, then the handler.</summary>
public sealed class NestedCallDepth8
{
    private readonly Behavior1 _first = new(new(new(new(new(new(new(new(new PingHandler()))))))));

    public ValueTask<int> Send(Ping request, CancellationToken cancellationToken) => _first.Handle(request, cancellationToken);

    private sealed class Behavior1(Behavior2 next)
    {
        private readonly Behavior2 _next = next;
        public int Calls;

        public ValueTask<int> Handle(Ping request, CancellationToken cancellationToken)
        {
            Calls++;
            return _next.Handle(request, cancellationToken);
        }
    }

    private sealed class Behavior2(Behavior3 next)
    {
        private readonly Behavior3 _next = next;
        public int Calls;

        public ValueTask<int> Handle(Ping request, CancellationToken cancellationToken)
        {
            Calls++;
            return _next.Handle(request, cancellationToken);
        }
    }

    private sealed class Behavior3(Behavior4 next)
    {
        private readonly Behavior4 _next = next;
        public int Calls;

        public ValueTask<int> Handle(Ping request, CancellationToken cancellationToken)
        {
            Calls++;
            return _next.Handle(request, cancellationToken);
        }
    }

    private sealed class Behavior4(Behavior5 next)
    {
        private readonly Behavior5 _next = next;
        public int Calls;

        public ValueTask<int> Handle(Ping request, CancellationToken cancellationToken)
        {
            Calls++;
            return _next.Handle(request, cancellationToken);
        }
    }

    private sealed class Behavior5(Behavior6 next)
    {
        private readonly Behavior6 _next = next;
        public int Calls;

        public ValueTask<int> Handle(Ping request, CancellationToken cancellationToken)
        {
            Calls++;
            return _next.Handle(request, cancellationToken);
        }
    }

    private sealed class Behavior6(Behavior7 next)
    {
        private readonly Behavior7 _next = next;
        public int Calls;

        public ValueTask<int> Handle(Ping request, CancellationToken cancellationToken)
        {
            Calls++;
            return _next.Handle(request, cancellationToken);
        }
    }

    private sealed class Behavior7(Behavior8 next)
    {
        private readonly Behavior8 _next = next;
        public int Calls;

        public ValueTask<int> Handle(Ping request, CancellationToken cancellationToken)
        {
            Calls++;
            return _next.Handle(request, cancellationToken);
        }
    }

    private sealed class Behavior8(PingHandler next)
    {
        private readonly PingHandler _next = next;
        public int Calls;

        public ValueTask<int> Handle(Ping request, CancellationToken cancellationToken)
        {
            Calls++;
            return _next.InvokeAsync(request, cancellationToken);
        }
    }
}
