using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Keryx.Benchmarks.MediatRHandlers;

/// <summary>Registers MediatR with this assembly's handlers and the pass-through behaviors, without a license key.</summary>
public static class MediatRSetup
{
    /// <summary>MediatR logs a license notice under this category when no key is set; benchmarking without a key is allowed.</summary>
    public const string LicenseLogCategory = "LuckyPennySoftware.MediatR.License";

    public static IServiceCollection AddMediatRBenchmarks(this IServiceCollection services)
    {
        services.AddLogging(static logging => logging.AddFilter(LicenseLogCategory, LogLevel.None));
        return services.AddMediatR(
            static configuration =>
            {
                configuration.RegisterServicesFromAssemblyContaining<Ping0>();
                configuration.AddOpenBehavior(typeof(Pass1<,>));
                configuration.AddOpenBehavior(typeof(Pass2<,>));
                configuration.AddOpenBehavior(typeof(Pass3<,>));
                configuration.AddOpenBehavior(typeof(Pass4<,>));
                configuration.AddOpenBehavior(typeof(Pass5<,>));
                configuration.AddOpenBehavior(typeof(Pass6<,>));
                configuration.AddOpenBehavior(typeof(Pass7<,>));
                configuration.AddOpenBehavior(typeof(Pass8<,>));
                configuration.AddOpenStreamBehavior(typeof(StreamPass1<,>));
                configuration.AddOpenStreamBehavior(typeof(StreamPass2<,>));
                configuration.AddOpenStreamBehavior(typeof(StreamPass3<,>));
            }
        );
    }
}
