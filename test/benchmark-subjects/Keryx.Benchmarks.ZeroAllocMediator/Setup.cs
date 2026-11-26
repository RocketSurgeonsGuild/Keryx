using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Benchmarks.ZeroAllocMediator;

/// <summary>
///     Registers this assembly's generated <c>IMediator</c> and handlers with ZeroAlloc.Mediator's defaults (transient). The generated
///     <c>AddMediator()</c> is internal, so the registration has to be made from here.
/// </summary>
public static class ZeroAllocSetup
{
    public static IServiceCollection AddZeroAllocBenchmarks(this IServiceCollection services) => services.AddMediator().Services;
}
