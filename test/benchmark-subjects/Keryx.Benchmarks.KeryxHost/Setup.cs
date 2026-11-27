using Keryx.Benchmarks.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Benchmarks.KeryxHost;

/// <summary>
///     Registers this host's generated mediator (scoped) and handlers, the real-world services and the scoped validator. Behaviors are static
///     and need no registration.
/// </summary>
public static class KeryxSetup
{
    public static IServiceCollection AddKeryxBenchmarks(this IServiceCollection services)
    {
        services.AddRealWorldServices();
        services.AddScoped<IValidator<TypicalPing>, TypicalPingValidator>();
        return services.AddKeryx();
    }
}
