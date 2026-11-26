using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Keryx.Analyzers.Diagnostics;

/// <summary>
///     KRX043: <c>[assembly: ExternallyHandled]</c>, <c>[assembly: ExcludeHandler]</c> or <c>[assembly: ExcludeBehavior]</c> in an assembly
///     without <c>[assembly: KeryxHost]</c> (OQ-54). Only a host reads them, so elsewhere they have no effect. Reported on each attribute.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HostOnlyAttributeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.HostOnlyAttributeOutsideHost];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(
            static startContext =>
            {
                if (!KeryxSymbols.SupportsLanguageVersion(startContext.Compilation)
                 || KeryxSymbols.Create(startContext.Compilation) is not { IsHost: false } symbols)
                {
                    return;
                }

                startContext.RegisterSyntaxNodeAction(nodeContext => Analyze(nodeContext, symbols), SyntaxKind.Attribute);
            }
        );
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, KeryxSymbols symbols)
    {
        var attribute = (AttributeSyntax)context.Node;
        if (attribute.Parent is not AttributeListSyntax { Target.Identifier.RawKind: (int)SyntaxKind.AssemblyKeyword }
         || context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol is not IMethodSymbol { ContainingType: var type })
        {
            return;
        }

        var name = Is(type, symbols.ExternallyHandledAttribute) ? "ExternallyHandled"
            : Is(type, symbols.ExcludeHandlerAttribute) ? "ExcludeHandler"
            : Is(type, symbols.ExcludeBehaviorAttribute) ? "ExcludeBehavior"
            : null;
        if (name is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.HostOnlyAttributeOutsideHost, attribute.GetLocation(), name));
        }
    }

    private static bool Is(INamedTypeSymbol type, INamedTypeSymbol? attributeType) =>
        attributeType is not null && SymbolEqualityComparer.Default.Equals(type, attributeType);
}
