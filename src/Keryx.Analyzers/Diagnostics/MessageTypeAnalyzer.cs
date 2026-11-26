using System.Collections.Immutable;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Keryx.Analyzers.Diagnostics;

/// <summary>
///     The rules on request, stream request and notification types: KRX011, KRX012 (types), KRX016 (the type and its response outside a
///     host) and KRX041. Also KRX016 for the behaviors a host's pipelines take from this assembly outside a host: <c>[Behavior]</c> on a
///     notification type (per-publish behaviors), <c>[assembly: Behavior]</c> and classes marked <c>[GlobalBehavior]</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MessageTypeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        Descriptors.ConflictingMessageInterfaces,
        Descriptors.UnsupportedTypeShape,
        Descriptors.NotEffectivelyPublic,
        Descriptors.MisplacedPublishStrategy,
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

                var isHost = symbols.IsHost;
                startContext.RegisterSymbolAction(symbolContext => Analyze(symbolContext, symbols, isHost), SymbolKind.NamedType);
                if (isHost)
                {
                    return;
                }

                if (symbols.GlobalBehaviorAttribute is not null)
                {
                    startContext.RegisterSymbolAction(symbolContext => AnalyzeGlobalBehavior(symbolContext, symbols), SymbolKind.NamedType);
                }

                // Assembly attributes have no symbol action; each one is reported from the syntax tree that declares it.
                var assemblyBehaviors = NonPublicBehaviors(startContext.Compilation.Assembly.GetAttributes(), symbols)
                                       .Where(static b => b.Location?.SourceTree is not null)
                                       .ToImmutableArray();
                if (!assemblyBehaviors.IsEmpty)
                {
                    startContext.RegisterSyntaxTreeAction(
                        treeContext =>
                        {
                            foreach (var (location, behaviorName) in assemblyBehaviors)
                            {
                                if (location!.SourceTree == treeContext.Tree)
                                {
                                    treeContext.ReportDiagnostic(
                                        Diagnostic.Create(Descriptors.NotEffectivelyPublic, location, behaviorName, "this assembly attaches it to its handlers and is not a host")
                                    );
                                }
                            }
                        }
                    );
                }
            }
        );
    }

    /// <summary>
    ///     The <c>[Behavior(typeof(X))]</c> attributes among <paramref name="attributes" /> whose behavior type is not effectively public, with
    ///     the attribute's location and the behavior's name.
    /// </summary>
    private static IEnumerable<(Location? Location, string BehaviorName)> NonPublicBehaviors(ImmutableArray<AttributeData> attributes, KeryxSymbols symbols)
    {
        foreach (var attribute in attributes)
        {
            if (KeryxSymbols.IsAttribute(attribute, symbols.BehaviorAttribute)
             && attribute.ConstructorArguments is [{ Value: ITypeSymbol behaviorType }]
             && TypeAccessibility.Of(behaviorType) != AccessLevel.Public)
            {
                yield return (HandlerAnalysis.AttributeLocation(attribute), behaviorType.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat));
            }
        }
    }

    private static void AnalyzeGlobalBehavior(SymbolAnalysisContext context, KeryxSymbols symbols)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (TypeAccessibility.OfDeclaration(type) == AccessLevel.Public
         || type.GetAttributes().FirstOrDefault(a => KeryxSymbols.IsAttribute(a, symbols.GlobalBehaviorAttribute)) is not { } globalBehavior)
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                Descriptors.NotEffectivelyPublic,
                HandlerAnalysis.AttributeLocation(globalBehavior) ?? type.Locations.FirstOrDefault(),
                type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                "it is a global behavior, every host that references this assembly applies it, and this assembly is not a host"
            )
        );
    }

    private static void Analyze(SymbolAnalysisContext context, KeryxSymbols symbols, bool isHost)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        var messageType = MessageType.Classify(type, symbols);
        var typeName = type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
        var location = type.Locations.FirstOrDefault();

        if (!messageType.IsConflicting
         && !( messageType.IsNotification && messageType.IsConcrete )
         && type.GetAttributes().FirstOrDefault(a => KeryxSymbols.IsAttribute(a, symbols.PublishStrategyAttribute)) is { } publishStrategy)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.MisplacedPublishStrategy, HandlerAnalysis.AttributeLocation(publishStrategy) ?? location, typeName));
        }

        if (!messageType.IsMessage || !messageType.IsConcrete)
        {
            return;
        }

        if (messageType.IsConflicting)
        {
            var interfaces = type.AllInterfaces
                                 .Where(i => KeryxSymbols.Is(i, symbols.RequestOfT)
                                          || KeryxSymbols.Is(i, symbols.StreamRequest)
                                          || SymbolEqualityComparer.Default.Equals(i, symbols.Notification))
                                 .Select(static i => i.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat))
                                 .Distinct(StringComparer.Ordinal)
                                 .OrderBy(static i => i, StringComparer.Ordinal);
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.ConflictingMessageInterfaces, location, typeName, string.Join(" and ", interfaces)));
            return;
        }

        if (messageType.IsUnsupportedShape)
        {
            var reason = type.IsRefLikeType
                ? "is a ref struct, so it cannot be a request or notification: a ref struct cannot be boxed, stored in a link or captured by an async method"
                : type.TypeParameters.Length > 0
                    ? "has type parameters, so it cannot be a request or notification; use a generic abstract base with closed subtypes instead"
                    : "is nested in a generic type, so it cannot be a request or notification";
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.UnsupportedTypeShape, location, typeName, reason));
            return;
        }

        if (isHost)
        {
            return;
        }

        var kind = messageType.IsRequest ? "request" : messageType.IsStream ? "stream request" : "notification";
        if (TypeAccessibility.OfDeclaration(type) != AccessLevel.Public)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.NotEffectivelyPublic, location, typeName, $"it is a {kind} and this assembly is not a host"));
        }

        if (messageType.IsNotificationOnly)
        {
            // Per-publish behaviors: the host builds the publish pipeline around the notification type.
            foreach (var (behaviorLocation, behaviorName) in NonPublicBehaviors(type.GetAttributes(), symbols))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        Descriptors.NotEffectivelyPublic,
                        behaviorLocation ?? location,
                        behaviorName,
                        $"the notification '{typeName}' attaches it and this assembly is not a host"
                    )
                );
            }
        }

        var response = messageType.IsRequest ? messageType.ResponseType : messageType.IsStream ? messageType.ItemType : null;
        if (response is not null && TypeAccessibility.Of(response) != AccessLevel.Public)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    Descriptors.NotEffectivelyPublic,
                    location,
                    response.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                    $"the {kind} '{typeName}' {( messageType.IsRequest ? "responds with" : "streams" )} it and this assembly is not a host"
                )
            );
        }
    }
}
