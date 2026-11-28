using System.Collections.Concurrent;
using System.Collections.Immutable;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Keryx.Analyzers.Diagnostics;

/// <summary>
///     The compilation-wide handler rules: KRX001 (a request without a handler in its assembly, in every assembly, hosts included) and the
///     same-assembly KRX005 (a request with more than one handler). Both need the whole compilation, so they are compilation-end diagnostics.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingHandlerAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.MissingHandler, Descriptors.DuplicateHandler];

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

                var requests = new ConcurrentBag<INamedTypeSymbol>();
                var handlers = new ConcurrentBag<(ITypeSymbol Request, IMethodSymbol Handler)>();
                startContext.RegisterSymbolAction(symbolContext => Collect((INamedTypeSymbol)symbolContext.Symbol, symbols, requests, handlers), SymbolKind.NamedType);
                startContext.RegisterCompilationEndAction(endContext => Report(endContext, symbols, requests, handlers));
            }
        );
    }

    private static void Collect(
        INamedTypeSymbol type,
        KeryxSymbols symbols,
        ConcurrentBag<INamedTypeSymbol> requests,
        ConcurrentBag<(ITypeSymbol Request, IMethodSymbol Handler)> handlers
    )
    {
        var messageType = MessageType.Classify(type, symbols);
        if (( messageType.IsRequest || messageType.IsStream ) && messageType.IsConcrete && !messageType.IsUnsupportedShape)
        {
            requests.Add(type);
        }

        foreach (var candidate in HandlerAnalysis.GetCandidates(type, symbols))
        {
            if (HandlerAnalysis.HandledRequestType(candidate, symbols) is { } requestType)
            {
                handlers.Add((requestType, candidate));
            }
        }
    }

    private static void Report(
        CompilationAnalysisContext context,
        KeryxSymbols symbols,
        ConcurrentBag<INamedTypeSymbol> requests,
        ConcurrentBag<(ITypeSymbol Request, IMethodSymbol Handler)> handlers
    )
    {
        var byRequest = handlers.GroupBy(static h => h.Request, SymbolEqualityComparer.Default).ToDictionary(static g => g.Key!, static g => g.ToArray(), SymbolEqualityComparer.Default);

        foreach (var group in byRequest.Values.Where(static g => g.Length > 1))
        {
            var requestName = group[0].Request.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
            var names = HandlerNames(group);
            foreach (var (_, handler) in group)
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.DuplicateHandler, handler.Locations.FirstOrDefault(), requestName, names));
            }
        }

        foreach (var request in requests)
        {
            if (!byRequest.ContainsKey(request))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Descriptors.MissingHandler, request.Locations.FirstOrDefault(), request.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat))
                );
            }
        }
    }

    private static string HandlerNames((ITypeSymbol Request, IMethodSymbol Handler)[] group)
    {
        var names = new string[group.Length];
        for (var i = 0; i < group.Length; i++)
        {
            var handler = group[i].Handler;
            names[i] = $"'{handler.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)}.{handler.Name}'";
        }

        Array.Sort(names, StringComparer.Ordinal);
        return string.Join(", ", names);
    }
}
