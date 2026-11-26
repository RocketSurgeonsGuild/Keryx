using Keryx.Benchmarks.ImmediateHandlers;
using Keryx.Benchmarks.KeryxHost;
using Keryx.Benchmarks.MediatorScoped;
using Keryx.Benchmarks.MediatorSingleton;
using Keryx.Benchmarks.MediatRHandlers;
using Keryx.Benchmarks.ZeroAllocMediator;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Benchmarks.Comparison;

/// <summary>
///     One container per library, built the way an application would build it, and the mediators the benchmarks call. Scoped mediators are
///     resolved once from one scope that lives as long as the benchmark (as in a request that sends many times); singletons from the root.
///     ZeroAlloc.Mediator's static <c>Mediator</c> class needs no container.
/// </summary>
internal sealed class Subjects : IDisposable
{
    private static readonly ServiceProviderOptions Validated = new() { ValidateScopes = true, ValidateOnBuild = true };
    private readonly List<IDisposable> _owned = [];
    private readonly IServiceScope _immediate;

    public Subjects()
    {
        var keryx = Scope(new ServiceCollection().AddKeryxBenchmarks());
        KeryxTyped = keryx.GetRequiredService<KeryxMediator>();
        KeryxGeneric = keryx.GetRequiredService<IMediator>();

        var mediatorScoped = Scope(new ServiceCollection().AddMediatorScopedBenchmarks());
        MediatorScopedTyped = mediatorScoped.GetRequiredService<MediatorScoped.Mediator>();
        MediatorScopedGeneric = mediatorScoped.GetRequiredService<Mediator.IMediator>();

        var mediatorSingleton = Root(new ServiceCollection().AddMediatorSingletonBenchmarks());
        MediatorSingletonTyped = mediatorSingleton.GetRequiredService<MediatorSingleton.Mediator>();
        MediatorSingletonGeneric = mediatorSingleton.GetRequiredService<Mediator.IMediator>();

        MediatR = Scope(new ServiceCollection().AddMediatRBenchmarks()).GetRequiredService<MediatR.IMediator>();

        var immediate = Root(new ServiceCollection().AddImmediateHandlersBenchmarks()).CreateScope();
        _owned.Add(immediate);
        _immediate = immediate;

        ZeroAllocMediator = Scope(new ServiceCollection().AddZeroAllocBenchmarks()).GetRequiredService<ZeroAlloc.Mediator.IMediator>();
    }

    public KeryxMediator KeryxTyped { get; }
    public IMediator KeryxGeneric { get; }
    public MediatorScoped.Mediator MediatorScopedTyped { get; }
    public Mediator.IMediator MediatorScopedGeneric { get; }
    public MediatorSingleton.Mediator MediatorSingletonTyped { get; }
    public Mediator.IMediator MediatorSingletonGeneric { get; }
    public MediatR.IMediator MediatR { get; }
    public ZeroAlloc.Mediator.IMediator ZeroAllocMediator { get; }

    /// <summary>An Immediate.Handlers generated handler class, from the scope that lives as long as the benchmark.</summary>
    public T Immediate<T>() where T : notnull => _immediate.ServiceProvider.GetRequiredService<T>();

    public void Dispose()
    {
        for (var i = _owned.Count - 1; i >= 0; i--)
        {
            _owned[i].Dispose();
        }

        _owned.Clear();
    }

    private ServiceProvider Root(IServiceCollection services)
    {
        var provider = services.BuildServiceProvider(Validated);
        _owned.Add(provider);
        return provider;
    }

    private IServiceProvider Scope(IServiceCollection services)
    {
        var scope = Root(services).CreateScope();
        _owned.Add(scope);
        return scope.ServiceProvider;
    }
}
