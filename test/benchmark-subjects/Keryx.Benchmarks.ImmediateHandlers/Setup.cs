using Immediate.Handlers.Shared;
using Microsoft.Extensions.DependencyInjection;

[assembly: ImmediateAssemblyIdentifier("Bench")]

namespace Keryx.Benchmarks.ImmediateHandlers;

/// <summary>Registers this assembly's generated handler classes and their behaviors.</summary>
public static class ImmediateHandlersSetup
{
    public static IServiceCollection AddImmediateHandlersBenchmarks(this IServiceCollection services) => services.AddBenchHandlers();
}
