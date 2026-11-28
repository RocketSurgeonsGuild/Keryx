using Keryx.Benchmarks.Common;
using MediatR.NotificationPublishers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Keryx.Benchmarks.MediatRHandlers;

/// <summary>
///     Registers MediatR with this assembly's handlers, the logging, validation and pass-through behaviors, the real-world services and the
///     scoped validator, without a license key.
/// </summary>
public static class MediatRSetup
{
    /// <summary>MediatR logs a license notice under this category when no key is set; benchmarking without a key is allowed.</summary>
    public const string LicenseLogCategory = "LuckyPennySoftware.MediatR.License";

    public static IServiceCollection AddMediatRBenchmarks(this IServiceCollection services)
    {
        services.AddRealWorldServices();
        services.AddLogging(static logging => logging.AddFilter(LicenseLogCategory, LogLevel.None));
        services.AddScoped<IValidator<TypicalPing>, TypicalPingValidator>();
        return services.AddMediatR(
            static configuration =>
            {
                configuration.RegisterServicesFromAssemblyContaining<Ping0>();
                configuration.AddOpenBehavior(typeof(LoggingBehavior<,>));
                configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
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

    /// <summary>
    ///     A second MediatR container for the real-world parallel publish scenario (owner decision 30d): the same handlers and real-world
    ///     services, with MediatR's <see cref="TaskWhenAllPublisher" /> as the notification publisher (a container-wide setting) and no
    ///     behaviors, which the scenario does not use.
    /// </summary>
    public static IServiceCollection AddMediatRParallelBenchmarks(this IServiceCollection services)
    {
        services.AddRealWorldServices();
        services.AddLogging(static logging => logging.AddFilter(LicenseLogCategory, LogLevel.None));
        return services.AddMediatR(
            static configuration =>
            {
                configuration.RegisterServicesFromAssemblyContaining<Ping0>();
                configuration.NotificationPublisherType = typeof(TaskWhenAllPublisher);
            }
        );
    }
}
