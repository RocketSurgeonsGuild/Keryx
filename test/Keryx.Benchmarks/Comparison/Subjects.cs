using Keryx.Benchmarks.Baseline;
using Keryx.Benchmarks.Common;
using Keryx.Benchmarks.ImmediateHandlers;
using Keryx.Benchmarks.KeryxHost;
using Keryx.Benchmarks.MediatorParallel;
using Keryx.Benchmarks.MediatorScoped;
using Keryx.Benchmarks.MediatorSingleton;
using Keryx.Benchmarks.MediatRHandlers;
using Keryx.Benchmarks.ZeroAllocMediator;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Benchmarks.Comparison;

/// <summary>
///     One container per library, built the way an application would build it, and the mediators the benchmarks call. Scoped mediators are
///     resolved once from one scope that lives as long as the benchmark (as in a request that sends many times); singletons from the root.
///     ZeroAlloc.Mediator's static <c>Mediator</c> class needs no container. The real-world scenarios also read each container's
///     long-lived scope (<see cref="LongLived" />) and its scope factory (<see cref="Scopes" />), to create a scope per request; the real-world
///     parallel publish scenario uses two more containers, Mediator with its <c>TaskWhenAllPublisher</c> (its own assembly) and MediatR with
///     its <c>TaskWhenAllPublisher</c>. Every container's SQLite connection is opened here, before any benchmark call. In a NativeAOT build the
///     containers of the libraries that are n/a there (<see cref="NativeAot" />) are not built.
/// </summary>
internal sealed class Subjects : IDisposable
{
    private static readonly ServiceProviderOptions Validated = new() { ValidateScopes = true, ValidateOnBuild = true };
    private readonly List<IDisposable> _owned = [];
    private readonly Dictionary<Library, (IServiceProvider Root, IServiceProvider LongLived)> _containers = [];

    public Subjects()
    {
        _ = Container(Library.Baseline, new ServiceCollection().AddBaselineBenchmarks());

        var keryx = Container(Library.Keryx, new ServiceCollection().AddKeryxBenchmarks());
        KeryxTyped = keryx.GetRequiredService<KeryxMediator>();
        KeryxGeneric = keryx.GetRequiredService<IMediator>();

        var mediatorScoped = Container(Library.MediatorScoped, new ServiceCollection().AddMediatorScopedBenchmarks());
        MediatorScopedTyped = mediatorScoped.GetRequiredService<MediatorScoped.Mediator>();
        MediatorScopedGeneric = mediatorScoped.GetRequiredService<Mediator.IMediator>();

        // Singletons, resolved from the root as an application would; the long-lived scope is for the real-world scenarios.
        _ = Container(Library.MediatorSingleton, new ServiceCollection().AddMediatorSingletonBenchmarks());
        var mediatorSingleton = _containers[Library.MediatorSingleton].Root;
        MediatorSingletonTyped = mediatorSingleton.GetRequiredService<MediatorSingleton.Mediator>();
        MediatorSingletonGeneric = mediatorSingleton.GetRequiredService<Mediator.IMediator>();

        // MediatR is n/a under NativeAOT (NativeAot.MediatRReason): there its containers are not built and its mediators stay null.
        if (!NativeAot.Skips(Library.MediatR))
        {
            MediatR = Container(Library.MediatR, new ServiceCollection().AddMediatRBenchmarks()).GetRequiredService<MediatR.IMediator>();
        }

        _ = Container(Library.ImmediateHandlers, new ServiceCollection().AddImmediateHandlersBenchmarks());

        ZeroAllocMediator = Container(Library.ZeroAlloc, new ServiceCollection().AddZeroAllocBenchmarks())
           .GetRequiredService<ZeroAlloc.Mediator.IMediator>();

        // The real-world parallel publish scenario's containers: singletons from the root, like Mediator singleton above.
        _ = Container(Library.MediatorParallel, new ServiceCollection().AddMediatorParallelBenchmarks());
        var mediatorParallel = _containers[Library.MediatorParallel].Root;
        MediatorParallelTyped = mediatorParallel.GetRequiredService<MediatorParallel.Mediator>();
        MediatorParallelGeneric = mediatorParallel.GetRequiredService<Mediator.IMediator>();

        if (!NativeAot.Skips(Library.MediatRParallel))
        {
            MediatRParallel = Container(Library.MediatRParallel, new ServiceCollection().AddMediatRParallelBenchmarks())
               .GetRequiredService<MediatR.IMediator>();
        }
    }

    public KeryxMediator KeryxTyped { get; }
    public IMediator KeryxGeneric { get; }
    public MediatorScoped.Mediator MediatorScopedTyped { get; }
    public Mediator.IMediator MediatorScopedGeneric { get; }
    public MediatorSingleton.Mediator MediatorSingletonTyped { get; }
    public Mediator.IMediator MediatorSingletonGeneric { get; }
    public MediatR.IMediator MediatR { get; } = null!;
    public ZeroAlloc.Mediator.IMediator ZeroAllocMediator { get; }
    public MediatorParallel.Mediator MediatorParallelTyped { get; }
    public Mediator.IMediator MediatorParallelGeneric { get; }
    public MediatR.IMediator MediatRParallel { get; } = null!;

    /// <summary>An Immediate.Handlers generated handler class, from the scope that lives as long as the benchmark.</summary>
    public T Immediate<T>() where T : notnull => LongLived(Library.ImmediateHandlers).GetRequiredService<T>();

    /// <summary>The provider of <paramref name="library" />'s scope that lives as long as the benchmark.</summary>
    public IServiceProvider LongLived(Library library) => _containers[library].LongLived;

    /// <summary>
    ///     <paramref name="library" />'s scope factory, for the scope-per-request scenarios; <see langword="null" /> for a library whose
    ///     container is not built (n/a under NativeAOT, <see cref="NativeAot" />), whose benchmarks do not run.
    /// </summary>
    public IServiceScopeFactory Scopes(Library library) =>
        _containers.TryGetValue(library, out var container) ? container.Root.GetRequiredService<IServiceScopeFactory>() : null!;

    public void Dispose()
    {
        for (var i = _owned.Count - 1; i >= 0; i--)
        {
            _owned[i].Dispose();
        }

        _owned.Clear();
        _containers.Clear();
    }

    /// <summary>
    ///     Builds the container, validated, opens its SQLite connection (creating and seeding the database the first time in the process), and
    ///     creates its long-lived scope; returns that scope's provider.
    /// </summary>
    private IServiceProvider Container(Library library, IServiceCollection services)
    {
        var root = services.BuildServiceProvider(Validated);
        _owned.Add(root);
        root.OpenPongDatabase();
        var scope = root.CreateScope();
        _owned.Add(scope);
        _containers.Add(library, (root, scope.ServiceProvider));
        return scope.ServiceProvider;
    }
}

/// <summary>The containers <see cref="Subjects" /> builds, one per library.</summary>
internal enum Library
{
    Baseline,
    Keryx,
    MediatorScoped,
    MediatorSingleton,
    MediatR,
    ImmediateHandlers,
    ZeroAlloc,
    MediatorParallel,
    MediatRParallel,
}
