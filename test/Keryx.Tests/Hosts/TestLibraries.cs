using System.Collections.Immutable;
using Keryx.Analyzers;
using Keryx.Tests.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Keryx.Tests.Hosts;

/// <summary>
///     Compiles the class libraries a test host references, the way a project that references the Keryx package compiles: the handler
///     generator runs, so the library carries its manifest (<c>[assembly: KeryxManifest]</c> and a <c>[KeryxHandler]</c> per public handler),
///     and with <c>host</c> the project is a host (<c>build_property.KeryxHost</c> = <c>true</c>) and the host generator runs too (an app host
///     that a test host references; its generated types are in the namespace of its assembly name).
/// </summary>
internal static class TestLibraries
{
    public static CSharpParseOptions ParseOptions { get; } = new(LanguageVersion.Latest);

    /// <summary>The references every library compiles against: the framework, Keryx and the DI abstractions.</summary>
    public static ImmutableArray<MetadataReference> References { get; } =
    [
        .. ManifestContractTests.FrameworkReferences,
        MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.ComponentModel.dll")),
        MetadataReference.CreateFromFile(typeof(Unit).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Collections.Frozen.FrozenDictionary).Assembly.Location),
    ];

    /// <summary>The library's compilation with the generators' output added, or an exception with the errors.</summary>
    public static Compilation Compilation(
        string assemblyName,
        string source,
        IEnumerable<MetadataReference>? references = null,
        bool generate = true,
        bool host = false
    )
    {
        Compilation compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, ParseOptions, $"{assemblyName}.cs")],
            [.. References, .. references ?? []],
            new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
        );
        if (generate)
        {
            ISourceGenerator[] generators = host
                ? [new HandlerGenerator().AsSourceGenerator(), new HostGenerator().AsSourceGenerator()]
                : [new HandlerGenerator().AsSourceGenerator()];
            _ = CSharpGeneratorDriver
               .Create(generators, parseOptions: ParseOptions, optionsProvider: host ? BuildProperties.Host : null)
               .RunGeneratorsAndUpdateCompilation(compilation, out compilation, out var generatorDiagnostics);
            if (generatorDiagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error))
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, generatorDiagnostics.Select(static d => d.ToString())));
            }
        }

        var errors = compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
        return errors.Length > 0 ? throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Select(static d => d.ToString()))) : compilation;
    }

    /// <summary>Compiles the library and returns its image.</summary>
    public static byte[] Image(string assemblyName, string source, IEnumerable<MetadataReference>? references = null, bool generate = true, bool host = false)
    {
        var compilation = Compilation(assemblyName, source, references, generate, host);
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        return emit.Success
            ? image.ToArray()
            : throw new InvalidOperationException(string.Join(Environment.NewLine, emit.Diagnostics.Select(static d => d.ToString())));
    }

    /// <summary>Compiles the library and returns a metadata reference to its image (a NuGet package or a built project reference).</summary>
    public static MetadataReference Reference(string assemblyName, string source, IEnumerable<MetadataReference>? references = null, bool generate = true, bool host = false) =>
        MetadataReference.CreateFromImage(Image(assemblyName, source, references, generate, host));
}
