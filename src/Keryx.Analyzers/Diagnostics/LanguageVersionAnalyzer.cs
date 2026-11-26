using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Keryx.Analyzers.Diagnostics;

/// <summary>
///     KRX022: the project compiles with a C# language version below 11. Reported once per compilation that references Keryx; every other
///     Keryx analyzer and generator stays silent in such a project, so this is the only Keryx error it sees.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LanguageVersionAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.LanguageVersionTooLow];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationAction(
            static compilationContext =>
            {
                if (compilationContext.Compilation is CSharpCompilation { LanguageVersion: < KeryxSymbols.MinimumLanguageVersion } compilation
                 && KeryxSymbols.Create(compilation) is not null)
                {
                    compilationContext.ReportDiagnostic(
                        Diagnostic.Create(Descriptors.LanguageVersionTooLow, Location.None, compilation.LanguageVersion.ToDisplayString())
                    );
                }
            }
        );
    }
}
