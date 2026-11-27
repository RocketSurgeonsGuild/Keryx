using Keryx.Benchmarks.Common;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Benchmarks.MediatorParallel;

/// <summary>
///     Registers this assembly's generated mediator with the singleton lifetime and Mediator's <see cref="TaskWhenAllPublisher" />, and the
///     real-world services.
/// </summary>
public static class MediatorParallelSetup
{
    public static IServiceCollection AddMediatorParallelBenchmarks(this IServiceCollection services)
    {
        services.AddRealWorldServices();
        // The generator reads these options from this call at compile time.
        return services.AddMediator(
            static (MediatorOptions options) =>
            {
                options.Namespace = "Keryx.Benchmarks.MediatorParallel";
                options.ServiceLifetime = ServiceLifetime.Singleton;
                options.NotificationPublisherType = typeof(TaskWhenAllPublisher);
            }
        );
    }
}
