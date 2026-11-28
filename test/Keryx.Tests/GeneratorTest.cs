using System.Runtime.Loader;
using Keryx.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rocket.Surgery.Extensions.Testing;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Keryx.Tests;

/// <summary>
///     The base of every test that compiles: <see cref="Builder" /> is the shared builder in a load context of the test's own, which every
///     compilation of the test (the libraries it references and the host) is loaded into, so a host resolves its libraries there by name.
/// </summary>
public abstract class GeneratorTest() : LoggerTest(Defaults.LoggerTest)
{
    [Before(Test)]
    public void InitializeBuilder()
    {
        Builder = GeneratorBuilderConstants.Builder.WithAssemblyLoadContext(AssemblyLoadContext);
        if (AssemblyLoadContext is IDisposable disposable)
        {
            Disposables.Add(disposable);
        }
    }

    protected AssemblyLoadContext AssemblyLoadContext { get; } = new CollectibleTestAssemblyLoadContext();
    protected GeneratorTestContextBuilder Builder { get; private set; } = null!;

    /// <summary>
    ///     Compiles a class library that a test compilation references, the way a project that references the Keryx package compiles (see
    ///     <see cref="LibraryKind" />), with nullable enabled, from one source named <c>{assemblyName}.cs</c>, and fails with its errors if it
    ///     does not compile. The library is loaded into the test's load context, where a host that references it resolves it by name, unless
    ///     <paramref name="ownLoadContext" /> gives it a load context of its own: a second build of an assembly name the test already built
    ///     (another version of it), which cannot share a load context with the first. <paramref name="languageVersion" /> sets the C# version
    ///     (the builder's default without it), for a library that uses a preview feature such as <c>closed</c> types.
    /// </summary>
    protected async Task<GeneratorTestResults> CompileLibraryAsync(
        string assemblyName,
        string source,
        CancellationToken cancellationToken,
        IEnumerable<GeneratorTestResults>? references = null,
        LibraryKind kind = LibraryKind.Handlers,
        bool ownLoadContext = false,
        LanguageVersion? languageVersion = null
    )
    {
        var builder = kind switch
        {
            LibraryKind.Plain => GeneratorBuilderConstants.References,
            LibraryKind.Handlers => GeneratorBuilderConstants.Builder,
            LibraryKind.Host => GeneratorBuilderConstants.Builder.WithGenerator<HostGenerator>().WithKeryxHost(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        if (languageVersion is { } version)
        {
            builder = builder.WithLanguageVersion(version);
        }

        var result = await builder.WithAssemblyLoadContext(ownLoadContext ? NewLoadContext() : AssemblyLoadContext)
                                  .WithProjectName(assemblyName)
                                  .WithNullableContext(NullableContextOptions.Enable)
                                  // FrozenDictionary, which a host's generic dispatch uses above the threshold.
                                  .AddReferences(typeof(System.Collections.Frozen.FrozenDictionary).Assembly)
                                  .AddCompilationReferences(references?.ToArray() ?? [])
                                  .AddSource($"{assemblyName}.cs", source)
                                  .Build()
                                  .GenerateAsync(cancellationToken);
        result.AssertCompilationWasSuccessful(DiagnosticSeverity.Error);
        return result;
    }

    /// <summary>A collectible load context disposed with the test, for a compilation that must not share the test's own.</summary>
    protected AssemblyLoadContext NewLoadContext()
    {
        var context = new CollectibleTestAssemblyLoadContext();
        Disposables.Add(context);
        return context;
    }

    /// <summary>How <see cref="CompileLibraryAsync" /> compiles a library.</summary>
    protected enum LibraryKind
    {
        /// <summary>With the handler generator: the library carries its Keryx manifest, as a project that references the Keryx package does.</summary>
        Handlers,

        /// <summary>
        ///     Without any generator: an assembly that references <c>Keryx.dll</c> but not the package (no manifest), or one whose manifest
        ///     the source writes by hand.
        /// </summary>
        Plain,

        /// <summary>
        ///     A host (<c>build_property.KeryxHost</c> = <c>true</c>): both generators run, and the generated host types are in the namespace
        ///     of its assembly name (an app host that a test host references).
        /// </summary>
        Host,
    }
}
