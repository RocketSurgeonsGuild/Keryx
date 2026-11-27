using Keryx.Benchmarks.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Benchmarks.ZeroAllocMediator;

/// <summary>
///     Registers this assembly's generated <c>IMediator</c> and handlers with ZeroAlloc.Mediator's defaults (transient), the real-world
///     services and the scoped validator. The generated <c>AddMediator()</c> and the static <c>Mediator</c> class are internal, so the
///     registration and the static path's factories have to be made from here.
/// </summary>
public static class ZeroAllocSetup
{
    public static IServiceCollection AddZeroAllocBenchmarks(this IServiceCollection services)
    {
        services.AddRealWorldServices();
        services.AddScoped<IValidator<TypicalPing>, TypicalPingValidator>();
        return services.AddMediator().Services;
    }

    /// <summary>
    ///     Gives the static <c>Mediator</c> path the real-world handlers that have constructor dependencies and that it runs (the data-access
    ///     handler, and the async notification handlers), through ZeroAlloc.Mediator's documented factory (<c>Mediator.Configure</c>,
    ///     <c>SetFactory</c>): each call resolves the transient handler from <paramref name="services" /> (the benchmark's long-lived scope).
    ///     The factories are process-wide, and the generated <c>IMediator</c>'s <c>Send</c> uses them too.
    /// </summary>
    public static void UseStaticFactories(IServiceProvider services) =>
        ZeroAlloc.Mediator.Mediator.Configure(
            configuration =>
            {
                configuration.SetFactory(() => services.GetRequiredService<QueryPingHandler>());
                configuration.SetFactory(() => services.GetRequiredService<QueryPinged1Handler>());
                configuration.SetFactory(() => services.GetRequiredService<QueryPinged5HandlerA>());
                configuration.SetFactory(() => services.GetRequiredService<QueryPinged5HandlerB>());
                configuration.SetFactory(() => services.GetRequiredService<QueryPinged5HandlerC>());
                configuration.SetFactory(() => services.GetRequiredService<QueryPinged5HandlerD>());
                configuration.SetFactory(() => services.GetRequiredService<QueryPinged5HandlerE>());
                configuration.SetFactory(() => services.GetRequiredService<ParallelQueryPinged1Handler>());
                configuration.SetFactory(() => services.GetRequiredService<ParallelQueryPinged5HandlerA>());
                configuration.SetFactory(() => services.GetRequiredService<ParallelQueryPinged5HandlerB>());
                configuration.SetFactory(() => services.GetRequiredService<ParallelQueryPinged5HandlerC>());
                configuration.SetFactory(() => services.GetRequiredService<ParallelQueryPinged5HandlerD>());
                configuration.SetFactory(() => services.GetRequiredService<ParallelQueryPinged5HandlerE>());
            }
        );
}
