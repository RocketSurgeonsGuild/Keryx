using System.Reflection;
using System.Runtime.Loader;
using Keryx.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Keryx.Tests.Hosts;

/// <summary>
///     Runs both generators over a host (<c>build_property.KeryxHost</c> = <c>true</c>, see <see cref="BuildProperties.WithKeryxHost" />), the
///     way a host project compiles: the handler generator emits the handler partials, the host generator the mediator and <c>AddKeryx()</c>.
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

    /// <summary>A host: both generators, <see cref="HostNamespace" /> as <c>RootNamespace</c> and <c>KeryxHost</c> set.</summary>
    protected GeneratorTestContextBuilder HostBuilder => HostGenerators.WithKeryxHost();

    /// <summary>
    ///     <see cref="HostBuilder" /> before <c>KeryxHost</c> is set: both generators over a project that is not a host unless the test
    ///     sets it (<see cref="BuildProperties.WithKeryxHost" />).
    /// </summary>
    protected GeneratorTestContextBuilder HostGenerators =>
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
        var compilation = result.FinalCompilation.WithOptions(result.FinalCompilation.Options.WithOptimizationLevel(OptimizationLevel.Release));
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
    ///     Compiles a host from the sources (after <see cref="Usings" />) against a referenced library (see <see cref="CompileLibrary" />), and
    ///     loads both into a load context of their own, where the host resolves the library by name.
    /// </summary>
    protected Task<Assembly> CompileHostAsync(string source, string libraryName, byte[] library, CancellationToken cancellationToken) =>
        CompileHostAsync(source, new Dictionary<string, byte[]>(StringComparer.Ordinal) { [libraryName] = library }, cancellationToken);

    /// <summary>
    ///     Compiles a host from the sources (after <see cref="Usings" />) against referenced libraries (by assembly name, see
    ///     <see cref="CompileLibrary" />), and loads them all into a load context of their own, where the host resolves each library by name.
    /// </summary>
    protected async Task<Assembly> CompileHostAsync(string source, IReadOnlyDictionary<string, byte[]> libraries, CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddReferences(libraries.Values.Select(static image => MetadataReference.CreateFromImage(image)))
                                      .AddSources(Usings + source)
                                      .Build()
                                      .GenerateAsync(cancellationToken);
        result.AssertCompilationWasSuccessful();
        return LoadHost(result, libraries, cancellationToken);
    }

    /// <summary>
    ///     Emits a generated host's final compilation, whatever its generators reported (a host whose generator reported an error such as
    ///     KRX034 still compiles), and loads it with the referenced libraries (by assembly name) into a load context of its own.
    /// </summary>
    protected Assembly LoadHost(GeneratorTestResults result, IReadOnlyDictionary<string, byte[]> libraries, CancellationToken cancellationToken)
    {
        using var image = new MemoryStream();
        var emit = result.FinalCompilation.Emit(image, cancellationToken: cancellationToken);
        if (!emit.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, emit.Diagnostics.Select(static d => d.ToString())));
        }

        var context = new LibraryLoadContext(libraries);
        Disposables.Add(context);
        image.Position = 0;
        return context.LoadFromStream(image);
    }

    /// <summary>
    ///     Compiles a library that a host references, against Keryx, with the handler generator, so it carries its manifest as a project that
    ///     references the Keryx package does, and returns its image.
    /// </summary>
    protected static byte[] CompileLibrary(string assemblyName, string source) => TestLibraries.Image(assemblyName, source);

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

    /// <summary>A collectible load context that resolves the referenced libraries from their images and everything else from the default context.</summary>
    private sealed class LibraryLoadContext(IReadOnlyDictionary<string, byte[]> libraries) : AssemblyLoadContext(isCollectible: true), IDisposable
    {
        private readonly Dictionary<string, Assembly> _loaded = [with(StringComparer.Ordinal)];

        public void Dispose() => Unload();

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is not { } name || !libraries.TryGetValue(name, out var library))
            {
                return null;
            }

            if (!_loaded.TryGetValue(name, out var assembly))
            {
                using var stream = new MemoryStream(library);
                assembly = LoadFromStream(stream);
                _loaded.Add(name, assembly);
            }

            return assembly;
        }
    }
}
