using MediatR;

namespace Keryx.Benchmarks.MediatRHandlers;

public sealed record Ping0(int Value) : IRequest<int>;
public sealed record Ping1(int Value) : IRequest<int>, IDepth1;
public sealed record Ping3(int Value) : IRequest<int>, IDepth1, IDepth3;
public sealed record Ping8(int Value) : IRequest<int>, IDepth1, IDepth3, IDepth8;
public sealed record YieldPing0(int Value) : IRequest<int>;
public sealed record YieldPing1(int Value) : IRequest<int>, IDepth1;
public sealed record YieldPing3(int Value) : IRequest<int>, IDepth1, IDepth3;
public sealed record YieldPing8(int Value) : IRequest<int>, IDepth1, IDepth3, IDepth8;

public sealed record Count0(int Count) : IStreamRequest<int>;
public sealed record Count3(int Count) : IStreamRequest<int>, IStreamDepth3;

public sealed record Pinged1(int Value) : INotification;
public sealed record Pinged5(int Value) : INotification;

// Markers that select the constrained open behaviors: a request with depth n implements every marker up to n.
public interface IDepth1;
public interface IDepth3;
public interface IDepth8;
public interface IStreamDepth3;
