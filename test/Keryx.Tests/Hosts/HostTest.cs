using System.Reflection;
using System.Runtime.CompilerServices;
using Keryx.Analyzers;
using Keryx.Analyzers.Hosts;
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

    /// <summary>The usings every test source starts with (<see cref="TestSources.Usings" />).</summary>
    protected const string Usings = TestSources.Usings;

    /// <summary>A host: both generators, <see cref="HostNamespace" /> as <c>RootNamespace</c> and <c>KeryxHost</c> set.</summary>
    protected GeneratorTestContextBuilder HostBuilder => HostGenerators.WithKeryxHost();

    /// <summary>
    ///     <see cref="HostBuilder" /> before <c>KeryxHost</c> is set: both generators over a project that is not a host unless the test
    ///     sets it (<see cref="BuildProperties.WithKeryxHost" />).
    /// </summary>
    protected GeneratorTestContextBuilder HostGenerators =>
        Builder.WithGenerator<HostGenerator>()
               .WithRootNamespace(HostNamespace)
               // FrozenDictionary, which generic dispatch uses above the threshold; a net10.0 host gets it from the shared framework.
               .AddReferences(typeof(System.Collections.Frozen.FrozenDictionary).Assembly);

    /// <summary>
    ///     Compiles a host from the sources (after <see cref="Usings" />) against the referenced libraries (see
    ///     <see cref="GeneratorTest.CompileLibraryAsync" />), and loads it into the test's load context, where it resolves them by name.
    /// </summary>
    protected async Task<Assembly> CompileHostAsync(string source, CancellationToken cancellationToken, params GeneratorTestResults[] libraries)
    {
        var result = await HostBuilder.AddCompilationReferences(libraries).AddSources(Usings + source).Build().GenerateAsync(cancellationToken);
        result.AssertCompilationWasSuccessful();
        return result.Assembly!;
    }

    /// <summary>
    ///     Compiles a host from the sources (after <see cref="Usings" />) with optimizations on, as a Release build would, and loads it.
    ///     Allocation tests of user-written <c>async</c> behaviors need it: a Debug build compiles an async state machine to a class,
    ///     allocated on every call even when the method completes synchronously.
    /// </summary>
    protected async Task<Assembly> CompileReleaseHostAsync(string source, CancellationToken cancellationToken)
    {
        var result = await HostBuilder.WithOptimizationLevel(OptimizationLevel.Release).AddSources(Usings + source).Build().GenerateAsync(cancellationToken);
        result.AssertCompilationWasSuccessful();
        return result.Assembly!;
    }

    /// <summary>
    ///     The one snapshot shape of host-generator output (testing-library review, convention 10): the generated mediator and
    ///     <c>AddKeryx()</c> as one object, in one <c>.verified.txt</c>. Every test that snapshots what the host generator emits goes through
    ///     it. Diagnostics are asserted by the test, not snapshotted, and the handler partials have their own snapshots (HandlerGeneratorTests).
    /// </summary>
    protected static SettingsTask VerifyHost(GeneratorTestResults result, [CallerFilePath] string sourceFile = "") =>
        Verify(
            new
            {
                Mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName),
                AddKeryx = TestSources.HostSource(result, HostEmitter.ServiceCollectionExtensionsHintName),
            },
            sourceFile: sourceFile
        );

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
