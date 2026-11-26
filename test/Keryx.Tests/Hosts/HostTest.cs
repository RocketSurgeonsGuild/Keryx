using System.Reflection;
using Keryx.Analyzers;
using Microsoft.Extensions.DependencyInjection;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Keryx.Tests.Hosts;

/// <summary>
///     Runs both generators over a host (<c>[assembly: KeryxHost]</c>), the way a host project compiles: the handler generator emits the
///     handler partials, the host generator the mediator and <c>AddKeryx()</c>.
/// </summary>
public abstract class HostTest : GeneratorTest
{
    /// <summary>The <c>RootNamespace</c> the hosts in these tests compile with, so the generated host types are <c>Sample.KeryxMediator</c>.</summary>
    protected const string HostNamespace = "Sample";

    protected const string Usings = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Keryx;
        using Microsoft.Extensions.DependencyInjection;

        """;

    protected GeneratorTestContextBuilder HostBuilder =>
        Builder.WithGenerator<HostGenerator>()
               .AddGlobalOption("build_property.RootNamespace", HostNamespace)
               // FrozenDictionary, which generic dispatch uses above the threshold; a net10.0 host gets it from the shared framework.
               .AddReferences(typeof(System.Collections.Frozen.FrozenDictionary).Assembly);

    /// <summary>Compiles a host from the sources (after <see cref="Usings" />) and loads it.</summary>
    protected async Task<Assembly> CompileHostAsync(string source, CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + source).Build().GenerateAsync(cancellationToken);
        result.AssertCompilationWasSuccessful();
        return result.Assembly!;
    }

    /// <summary>
    ///     A root provider with <c>AddKeryx()</c> applied (twice, which must change nothing) plus the test's own registrations. Scopes are
    ///     validated, so resolving the scoped mediator from the root fails.
    /// </summary>
    protected static ServiceProvider BuildProvider(Assembly host, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);
        AddKeryx(host, services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    protected static void AddKeryx(Assembly host, IServiceCollection services) =>
        host.GetType($"{HostNamespace}.KeryxServiceCollectionExtensions", throwOnError: true)!
            .GetMethod("AddKeryx", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [services]);

    protected static object Create(Assembly assembly, string typeName, params object?[] arguments) =>
        Activator.CreateInstance(assembly.GetType(typeName, throwOnError: true)!, arguments)!;

    /// <summary>Calls a public static method of a type in the compiled host (the probes the sources declare).</summary>
    protected static T Call<T>(Assembly assembly, string typeName, string methodName, params object?[] arguments) =>
        (T)assembly.GetType(typeName, throwOnError: true)!.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, arguments)!;
}
