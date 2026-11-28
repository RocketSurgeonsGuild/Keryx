using Keryx.Benchmarks.Common;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Benchmarks.MediatorSingleton;

/// <summary>
///     Registers this assembly's generated mediator with the singleton lifetime, the pass-through behaviors and the real-world
///     services.
/// </summary>
public static class MediatorSingletonSetup
{
    public static IServiceCollection AddMediatorSingletonBenchmarks(this IServiceCollection services)
    {
        services.AddRealWorldServices();
        // The generator reads these options from this call at compile time.
        return services.AddMediator(
            static (MediatorOptions options) =>
            {
                options.Namespace = "Keryx.Benchmarks.MediatorSingleton";
                options.ServiceLifetime = ServiceLifetime.Singleton;
                options.PipelineBehaviors = [typeof(Pass1<,>), typeof(Pass2<,>), typeof(Pass3<,>), typeof(Pass4<,>), typeof(Pass5<,>), typeof(Pass6<,>), typeof(Pass7<,>), typeof(Pass8<,>)];
                options.StreamPipelineBehaviors = [typeof(StreamPass1<,>), typeof(StreamPass2<,>), typeof(StreamPass3<,>)];
            }
        );
    }
}
