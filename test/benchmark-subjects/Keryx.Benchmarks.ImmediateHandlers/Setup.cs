using Immediate.Handlers.Shared;
using Keryx.Benchmarks.Common;
using Microsoft.Extensions.DependencyInjection;

[assembly: ImmediateAssemblyIdentifier("Bench")]

namespace Keryx.Benchmarks.ImmediateHandlers;

/// <summary>Registers this assembly's generated handler classes and their behaviors, the real-world services and the scoped validator.</summary>
public static class ImmediateHandlersSetup
{
    public static IServiceCollection AddImmediateHandlersBenchmarks(this IServiceCollection services)
    {
        services.AddRealWorldServices();
        services.AddScoped<IValidator<TypicalPing>, TypicalPingValidator>();
        return services.AddBenchHandlers();
    }
}
