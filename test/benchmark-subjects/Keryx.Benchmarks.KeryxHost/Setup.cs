using Keryx;
using Microsoft.Extensions.DependencyInjection;

[assembly: KeryxHost]

namespace Keryx.Benchmarks.KeryxHost;

/// <summary>Registers this host's generated mediator (scoped) and handlers. Behaviors are static and need no registration.</summary>
public static class KeryxSetup
{
    public static IServiceCollection AddKeryxBenchmarks(this IServiceCollection services) => services.AddKeryx();
}
