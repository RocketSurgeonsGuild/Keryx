using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Keryx.Benchmarks.Common;

/// <summary>The services every subject's real-world scenarios use, registered the same way in each library's container.</summary>
public static class RealWorldServices
{
    /// <summary>
    ///     Logging to an <see cref="InMemoryLoggerProvider" /> at <see cref="LogLevel.Information" /> (no other provider), the scoped
    ///     <see cref="IPongRepository" />, and a singleton open <see cref="SqliteConnection" /> to <see cref="PongDatabase" />. Each subject
    ///     registers its own <c>IValidator&lt;TypicalPing&gt;</c> (scoped, the lifetime FluentValidation's assembly scan uses).
    /// </summary>
    public static IServiceCollection AddRealWorldServices(this IServiceCollection services)
    {
        services.AddLogging(
            static logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddProvider(new InMemoryLoggerProvider());
            }
        );
        services.TryAddScoped<IPongRepository, PongRepository>();
        services.TryAddSingleton(static _ => PongDatabase.Open());
        return services;
    }

    /// <summary>
    ///     Resolves the container's singleton <see cref="SqliteConnection" />, which opens it and, the first time in the process, creates and
    ///     seeds <see cref="PongDatabase" />. The benchmarks call it in setup for every container, so no subject opens its connection or seeds
    ///     the database inside a measured call.
    /// </summary>
    public static void OpenPongDatabase(this IServiceProvider services) => _ = services.GetRequiredService<SqliteConnection>();
}
