namespace Keryx.Benchmarks.ImmediateHandlers;

// Immediate.Handlers takes any type as a request.

public sealed record Ping0(int Value);
public sealed record Ping1(int Value);
public sealed record Ping3(int Value);
public sealed record Ping8(int Value);
public sealed record YieldPing0(int Value);
public sealed record YieldPing1(int Value);
public sealed record YieldPing3(int Value);
public sealed record YieldPing8(int Value);

public sealed record Count0(int Count);
public sealed record Count3(int Count);
