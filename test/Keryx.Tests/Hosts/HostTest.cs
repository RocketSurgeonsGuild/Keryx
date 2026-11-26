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
    ///     Compiles a host from the sources (after <see cref="Usings" />) with optimizations on, as a Release build would, and loads it into a
    ///     load context of its own. Allocation tests of user-written <c>async</c> behaviors need it: a Debug build compiles an async state
    ///     machine to a class, allocated on every call even when the method completes synchronously.
    /// </summary>
    protected async Task<Assembly> CompileReleaseHostAsync(string source, CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + source).Build().GenerateAsync(cancellationToken);
        result.AssertCompilationWasSuccessful();
        var compilation = result.FinalCompilation.WithOptions(result.FinalCompilation.Options.WithOptimizationLevel(Microsoft.CodeAnalysis.OptimizationLevel.Release));
        using var image = new MemoryStream();
        var emit = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emit.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, emit.Diagnostics.Select(static d => d.ToString())));
        }

        var context = new CollectibleTestAssemblyLoadContext();
        Disposables.Add(context);
        image.Position = 0;
        return context.LoadFromStream(image);
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
