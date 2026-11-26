using System.Collections.Immutable;
using Keryx.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Keryx.Tests;

/// <summary>
///     Compiles a source and runs the handler generator and an analyzer over it directly, for the compilation options the source generator
///     testing helpers do not expose (nullable context, unsafe code).
/// </summary>
internal static class DirectCompilation
{
    public static CSharpParseOptions ParseOptions { get; } = new(LanguageVersion.Latest);

    private static MetadataReference[] References { get; } =
    [
        .. Abstractions.ManifestContractTests.FrameworkReferences,
        MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.ComponentModel.dll")),
        MetadataReference.CreateFromFile(typeof(Unit).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute).Assembly.Location),
    ];

    public static CSharpCompilation Create(string source, NullableContextOptions nullable = NullableContextOptions.Enable, bool allowUnsafe = false) =>
        CSharpCompilation.Create(
            "Direct",
            [CSharpSyntaxTree.ParseText(source, ParseOptions, "Input0.cs")],
            References,
            new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: nullable, allowUnsafe: allowUnsafe)
        );

    /// <summary>Runs the handler generator and returns the compilation with its output, and the generated sources.</summary>
    public static (Compilation Output, ImmutableArray<GeneratedSourceResult> Sources) Generate(Compilation compilation, CancellationToken cancellationToken)
    {
        var driver = CSharpGeneratorDriver
                    .Create([new HandlerGenerator().AsSourceGenerator()], parseOptions: ParseOptions)
                    .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics, cancellationToken);

        return !generatorDiagnostics.IsEmpty
            ? throw new InvalidOperationException(string.Join(Environment.NewLine, generatorDiagnostics.Select(static d => d.ToString())))
            : ((Compilation Output, ImmutableArray<GeneratedSourceResult> Sources))(output, driver.GetRunResult().Results.Single().GeneratedSources);
    }

    public static Task<ImmutableArray<Diagnostic>> AnalyzeAsync<TAnalyzer>(Compilation compilation, CancellationToken cancellationToken)
        where TAnalyzer : DiagnosticAnalyzer, new() =>
        compilation.WithAnalyzers([new TAnalyzer()]).GetAnalyzerDiagnosticsAsync(cancellationToken);
}
