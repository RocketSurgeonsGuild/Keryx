using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Keryx.Analyzers.Diagnostics;

/// <summary>
///     KRX021: a struct request passed to <c>ISender.Send</c> or <c>ISender.Stream</c> (also through <c>IMediator</c>) is boxed at the call
///     site. Reported on the argument.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StructRequestAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.BoxedStructRequest];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(
            static startContext =>
            {
                if (!KeryxSymbols.SupportsLanguageVersion(startContext.Compilation)
                 || KeryxSymbols.Create(startContext.Compilation) is not { Sender: { } sender })
                {
                    return;
                }

                startContext.RegisterOperationAction(operationContext => Analyze(operationContext, sender), OperationKind.Invocation);
            }
        );
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol sender)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name is not ( "Send" or "Stream" ) || !SymbolEqualityComparer.Default.Equals(method.ContainingType, sender))
        {
            return;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Ordinal != 0 || argument.Value is not IConversionOperation { Operand.Type: { IsValueType: true } requestType, Type.IsValueType: false })
            {
                continue;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    Descriptors.BoxedStructRequest,
                    argument.Syntax.GetLocation(),
                    requestType.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                    $"ISender.{method.Name}"
                )
            );

            // Only the request argument binds to parameter 0.
            return;
        }
    }
}
