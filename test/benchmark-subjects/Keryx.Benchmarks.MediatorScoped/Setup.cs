using Keryx.Benchmarks.Common;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Benchmarks.MediatorScoped;

/// <summary>
///     Registers this assembly's generated mediator with the scoped lifetime, the logging, validation and pass-through behaviors,
///     the real-world services and the scoped validator.
/// </summary>
public static class MediatorScopedSetup
{
    public static IServiceCollection AddMediatorScopedBenchmarks(this IServiceCollection services)
    {
        services.AddRealWorldServices();
        services.AddScoped<IValidator<TypicalPing>, TypicalPingValidator>();
        // The generator reads these options from this call at compile time.
        return services.AddMediator(
            static (MediatorOptions options) =>
            {
                options.Namespace = "Keryx.Benchmarks.MediatorScoped";
                options.ServiceLifetime = ServiceLifetime.Scoped;
                options.PipelineBehaviors = [typeof(LoggingBehavior<,>), typeof(ValidationBehavior<,>), typeof(Pass1<,>), typeof(Pass2<,>), typeof(Pass3<,>), typeof(Pass4<,>), typeof(Pass5<,>), typeof(Pass6<,>), typeof(Pass7<,>), typeof(Pass8<,>)];
                options.StreamPipelineBehaviors = [typeof(StreamPass1<,>), typeof(StreamPass2<,>), typeof(StreamPass3<,>)];
            }
        );
    }
}
