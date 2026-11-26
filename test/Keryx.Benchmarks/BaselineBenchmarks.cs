using BenchmarkDotNet.Attributes;

namespace Keryx.Benchmarks;

/// <summary>
///     A placeholder: the hand-written direct call every mediator benchmark is measured against.
/// </summary>
[MemoryDiagnoser]
public class BaselineBenchmarks
{
    private readonly Request _request = new(21);

    [Benchmark(Baseline = true)]
    public ValueTask<int> DirectCall() => Handle(_request);

    private static ValueTask<int> Handle(Request request) => new(request.Value * 2);

    private sealed record Request(int Value);
}
