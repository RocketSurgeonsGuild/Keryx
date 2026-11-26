using System.Collections.Immutable;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Keryx.Analyzers.Diagnostics;

/// <summary>
///     The rules on handlers and handler classes: KRX002, KRX003, KRX004, KRX006, KRX007, KRX012 (handler classes), KRX013, KRX014, KRX015,
///     KRX016 (handler parameters and attached behaviors outside a host), KRX017, KRX018, KRX023 and KRX040.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HandlerAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        Descriptors.InvalidHandlerSignature,
        Descriptors.InvalidHandlerParameter,
        Descriptors.InvalidHandlerClass,
        Descriptors.MultipleHandlersInClass,
        Descriptors.InvalidHandlerConstructor,
        Descriptors.UnsupportedTypeShape,
        Descriptors.InvalidHandlerParameterKind,
        Descriptors.StaticHandlerClassWithDependencies,
        Descriptors.ReservedHandlerMember,
        Descriptors.NotEffectivelyPublic,
        Descriptors.ResponseNullabilityMismatch,
        Descriptors.InvalidServiceKey,
        Descriptors.ForeignRequestHandler,
        Descriptors.MisplacedHandlerOrder,
    ];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(
            static startContext =>
            {
                if (!KeryxSymbols.SupportsLanguageVersion(startContext.Compilation) || KeryxSymbols.Create(startContext.Compilation) is not { } symbols)
                {
                    return;
                }

                var isHost = KeryxBuildProperties.IsHost(startContext.Options.AnalyzerConfigOptionsProvider.GlobalOptions);
                startContext.RegisterSymbolAction(symbolContext => AnalyzeType(symbolContext, symbols, isHost), SymbolKind.NamedType);
                if (symbols.HandlerOrderAttribute is not null)
                {
                    startContext.RegisterSymbolAction(symbolContext => AnalyzeHandlerOrder(symbolContext, symbols), SymbolKind.Method);
                }
            }
        );
    }

    private static void AnalyzeType(SymbolAnalysisContext context, KeryxSymbols symbols, bool isHost)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        var candidates = HandlerAnalysis.GetCandidates(type, symbols);
        if (candidates.IsEmpty && !HandlerAnalysis.IsOrContainsHandlerClass(type, symbols))
        {
            return;
        }

        var issues = new IssueCollector();
        HandlerAnalysis.AnalyzeContainingChainType(type, !candidates.IsEmpty, issues);
        if (!candidates.IsEmpty)
        {
            var signatures = new List<HandlerSignature>(candidates.Length);
            foreach (var candidate in candidates)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                signatures.Add(HandlerAnalysis.AnalyzeMethod(candidate, symbols, issues));
            }

            HandlerAnalysis.AnalyzeClass(type, signatures, symbols, issues);
        }

        issues.ReportTo(context.ReportDiagnostic, isHost);
    }

    private static void AnalyzeHandlerOrder(SymbolAnalysisContext context, KeryxSymbols symbols)
    {
        var method = (IMethodSymbol)context.Symbol;
        var handlerOrder = method.GetAttributes().FirstOrDefault(a => KeryxSymbols.IsAttribute(a, symbols.HandlerOrderAttribute));
        if (handlerOrder is null || IsNotificationHandler(method, symbols))
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                Descriptors.MisplacedHandlerOrder,
                HandlerAnalysis.AttributeLocation(handlerOrder) ?? method.Locations.FirstOrDefault(),
                method.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)
            )
        );
    }

    private static bool IsNotificationHandler(IMethodSymbol method, KeryxSymbols symbols)
    {
        if (!HandlerAnalysis.IsCandidate(method, symbols))
        {
            return false;
        }

        var messageTypes = method.Parameters.Select(p => MessageType.Classify(p.Type, symbols)).Where(static m => m.IsMessage).ToArray();
        return messageTypes is [{ IsNotificationOnly: true }];
    }
}
