using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Benchmarks.MediatorScoped;

/// <summary>Registers this assembly's generated mediator with the scoped lifetime and the pass-through behaviors.</summary>
public static class MediatorScopedSetup
{
    // The generator reads these options from this call at compile time.
    public static IServiceCollection AddMediatorScopedBenchmarks(this IServiceCollection services) =>
        services.AddMediator(
            static (MediatorOptions options) =>
            {
                options.Namespace = "Keryx.Benchmarks.MediatorScoped";
                options.ServiceLifetime = ServiceLifetime.Scoped;
                options.PipelineBehaviors = [typeof(Pass1<,>), typeof(Pass2<,>), typeof(Pass3<,>), typeof(Pass4<,>), typeof(Pass5<,>), typeof(Pass6<,>), typeof(Pass7<,>), typeof(Pass8<,>)];
                options.StreamPipelineBehaviors = [typeof(StreamPass1<,>), typeof(StreamPass2<,>), typeof(StreamPass3<,>)];
            }
        );
}
