using Keryx;
using Microsoft.Extensions.DependencyInjection;

[assembly: KeryxHost]

namespace Keryx.Benchmarks.KeryxHost;

/// <summary>Registers this host's generated mediator (scoped), handlers and behaviors.</summary>
public static class KeryxSetup
{
    public static IServiceCollection AddKeryxBenchmarks(this IServiceCollection services) => services.AddKeryx();
}
