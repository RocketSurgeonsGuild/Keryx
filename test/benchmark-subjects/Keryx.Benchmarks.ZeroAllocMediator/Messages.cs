using ZeroAlloc.Mediator;

namespace Keryx.Benchmarks.ZeroAllocMediator;

public sealed record Ping0(int Value) : IRequest<int>;
public sealed record Ping1(int Value) : IRequest<int>;
public sealed record Ping3(int Value) : IRequest<int>;
public sealed record Ping8(int Value) : IRequest<int>;
public sealed record YieldPing0(int Value) : IRequest<int>;
public sealed record YieldPing1(int Value) : IRequest<int>;
public sealed record YieldPing3(int Value) : IRequest<int>;
public sealed record YieldPing8(int Value) : IRequest<int>;

public sealed record Count0(int Count) : IStreamRequest<int>;
public sealed record Count3(int Count) : IStreamRequest<int>;

public sealed record Pinged1(int Value) : INotification;
public sealed record Pinged5(int Value) : INotification;
