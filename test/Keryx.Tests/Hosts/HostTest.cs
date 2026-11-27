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
    ///     A handler library whose request and stream request types are not all sealed, as one built with KRX024 suppressed has them (OQ-68):
    ///     <c>Get</c> and <c>Items</c> are not sealed; <c>Ping</c> is sealed, <c>Ship</c> is a sealed case of the abstract <c>Command</c>,
    ///     <c>Circle</c> of the closed (C# 15) <c>Shape</c>, and <c>Noted</c> is a notification, which need not be sealed.
    /// </summary>
    protected static readonly string UnsealedLibrary = TestSources.ClosedTypeAttribute
                                                     + """
                                                       namespace Open
                                                       {
                                                           using System.Collections.Generic;
                                                           using System.Threading.Tasks;
                                                           using Keryx;

                                                           public record Get(int Id) : IRequest<string>;
                                                           public static partial class GetHandler { private static string Handle(Get request) => "get " + request.Id; }

                                                           public record Items(int Count) : IStreamRequest<int>;
                                                           public static partial class ItemsHandler { private static IAsyncEnumerable<int> Handle(Items request) => null!; }

                                                           public sealed record Ping(int Id) : IRequest<string>;
                                                           public static partial class PingHandler { private static string Handle(Ping request) => "ping " + request.Id; }

                                                           public abstract record Command : IRequest<string>;
                                                           public sealed record Ship(int Id) : Command;
                                                           public static partial class ShipHandler { private static string Handle(Ship request) => "ship " + request.Id; }

                                                           public closed record Shape : IRequest<string>;
                                                           public sealed record Circle(int Radius) : Shape;
                                                           public static partial class CircleHandler { private static string Handle(Circle request) => "circle " + request.Radius; }

                                                           public record Noted(int Id) : INotification;
                                                           public static partial class NotedHandler { private static ValueTask Handle(Noted notification) => default; }
                                                       }
                                                       """;

    /// <summary>
    ///     Compiles <see cref="UnsealedLibrary" /> as a library project that set <c>dotnet_diagnostic.KRX024.severity = none</c> compiles: with
    ///     the handler generator and the <c>MessageTypeAnalyzer</c>, at C# 15 (preview) for its closed type, and fails unless the analyzer
    ///     reports nothing.
    /// </summary>
    protected async Task<GeneratorTestResults> CompileUnsealedLibraryAsync(CancellationToken cancellationToken)
    {
        var result = await GeneratorBuilderConstants.Builder.WithAnalyzer<Keryx.Analyzers.Diagnostics.MessageTypeAnalyzer>()
                                                    .AddGlobalOption("dotnet_diagnostic.KRX024.severity", "none")
                                                    .WithLanguageVersion(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview)
                                                    .WithAssemblyLoadContext(AssemblyLoadContext)
                                                    .WithProjectName("Open")
                                                    .WithNullableContext(NullableContextOptions.Enable)
                                                    .AddSource("Open.cs", UnsealedLibrary)
                                                    .Build()
                                                    .GenerateAsync(cancellationToken);
        result.AssertCompilationWasSuccessful(DiagnosticSeverity.Error);
        await Assert.That(result.AnalyzerResults[typeof(Keryx.Analyzers.Diagnostics.MessageTypeAnalyzer)].Diagnostics).IsEmpty();
        return result;
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
    ///     it (or through <see cref="VerifyMediator" />, its mediator half). Diagnostics are asserted by the test, not snapshotted, and the handler partials have their own snapshots (HandlerGeneratorTests).
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
    ///     The <c>Mediator</c> half of <see cref="VerifyHost" /> alone, in the same shape, for tests about generic dispatch only
    ///     (JumpTableDispatchTests), where <c>AddKeryx()</c> would only repeat one registration per handler.
    /// </summary>
    protected static SettingsTask VerifyMediator(GeneratorTestResults result, [CallerFilePath] string sourceFile = "") =>
        Verify(new { Mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName) }, sourceFile: sourceFile);

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
