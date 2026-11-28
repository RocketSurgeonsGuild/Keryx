using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Benchmarks.Host;

/// <summary>
///     How the scoped mediator reaches a handler on each call (ADR 0001, Mediator): a pipeline context created lazily once per scope with
///     <c>Volatile.Read(ref field) ?? Interlocked.CompareExchange(...)</c>, against resolving the handler with <c>GetRequiredService</c> on every
///     call. Hand-written in the shape the host generator emits; the numbers are recorded in open-questions OQ-15.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByJob)]
public class PipelineContextBenchmarks : IDisposable
{
    private readonly Ping _request = new(21);
    private ServiceProvider _root = null!;
    private AsyncServiceScope _scope;
    private LazyContextMediator _lazy = null!;
    private ResolvePerCallMediator _resolve = null!;
    private PingHandler _handler = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddScoped(static _ => new Clock());
        services.AddScoped(static sp => new PingHandler(sp.GetRequiredService<Clock>()));
        _root = services.BuildServiceProvider();
        _scope = _root.CreateAsyncScope();
        _lazy = new(_scope.ServiceProvider);
        _resolve = new(_scope.ServiceProvider);
        _handler = _scope.ServiceProvider.GetRequiredService<PingHandler>();
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _scope.Dispose();
        _root.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The handler instance held by the caller: the floor for both shapes.</summary>
    [Benchmark]
    public int Direct() => Completed.Result(_handler.InvokeAsync(_request, CancellationToken.None));

    [Benchmark(Baseline = true)]
    public int LazyPipelineContext() => Completed.Result(_lazy.Send(_request, CancellationToken.None));

    [Benchmark]
    public int GetRequiredServicePerCall() => Completed.Result(_resolve.Send(_request, CancellationToken.None));

    private sealed record Ping(int Value);

    private sealed class Clock;

    private sealed class PingHandler(Clock clock)
    {
        public Clock Clock { get; } = clock;

        public ValueTask<int> InvokeAsync(Ping request, CancellationToken cancellationToken) => new(request.Value * 2);
    }

    private sealed class LazyContextMediator(IServiceProvider serviceProvider)
    {
        private Context? _context;

        public ValueTask<int> Send(Ping request, CancellationToken cancellationToken) =>
            ( Volatile.Read(ref _context) ?? CreateContext() ).Handler.InvokeAsync(request, cancellationToken);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private Context CreateContext() => Interlocked.CompareExchange(ref _context, new(serviceProvider), null) ?? _context!;

        private sealed class Context(IServiceProvider serviceProvider)
        {
            public readonly PingHandler Handler = serviceProvider.GetRequiredService<PingHandler>();
        }
    }

    private sealed class ResolvePerCallMediator(IServiceProvider serviceProvider)
    {
        public ValueTask<int> Send(Ping request, CancellationToken cancellationToken) =>
            serviceProvider.GetRequiredService<PingHandler>().InvokeAsync(request, cancellationToken);
    }
}
