using System.Collections.Concurrent;
using System.Collections.Immutable;
using Keryx.Analyzers.Behaviors;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Keryx.Analyzers.Diagnostics;

/// <summary>
///     The rules on behaviors (ADR 0001, Behaviors, Ordering): KRX019 and KRX020 (and KRX018 for a keyed constructor parameter) on behavior
///     classes, KRX009 for a behavior attached to a handler method or notification type that does not fit it, KRX042 for <c>[Behavior]</c> or
///     <c>[GlobalBehavior]</c> where it does nothing, KRX016 in a host for a behavior generated code cannot reach, and, at compilation end,
///     KRX008 for <c>RunsAfter</c>/<c>RunsBefore</c> edges between behaviors declared in this compilation's source.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BehaviorAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        Descriptors.InvalidBehaviorOrdering,
        Descriptors.BehaviorDoesNotFit,
        Descriptors.NotEffectivelyPublic,
        Descriptors.InvalidServiceKey,
        Descriptors.InvalidBehaviorShape,
        Descriptors.InvalidBehaviorConstructor,
        Descriptors.MisplacedBehaviorAttribute,
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
                var behaviors = new ConcurrentBag<INamedTypeSymbol>();
                startContext.RegisterSymbolAction(symbolContext => AnalyzeType(symbolContext, symbols, isHost, behaviors), SymbolKind.NamedType);
                if (symbols.BehaviorAttribute is not null)
                {
                    startContext.RegisterSymbolAction(symbolContext => AnalyzeMethod(symbolContext, symbols, isHost), SymbolKind.Method);
                }

                startContext.RegisterCompilationEndAction(endContext => AnalyzeOrdering(endContext, symbols, behaviors));
            }
        );
    }

    private static void AnalyzeType(SymbolAnalysisContext context, KeryxSymbols symbols, bool isHost, ConcurrentBag<INamedTypeSymbol> behaviors)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        var typeName = type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
        if (BehaviorShapes.IsBehaviorCandidate(type, symbols))
        {
            behaviors.Add(type);
            var issues = new IssueCollector();
            BehaviorShapes.Analyze(type, symbols, issues);
            issues.ReportTo(context.ReportDiagnostic, isHost);
        }

        foreach (var attribute in type.GetAttributes())
        {
            if (KeryxSymbols.IsAttribute(attribute, symbols.GlobalBehaviorAttribute))
            {
                var location = HandlerAnalysis.AttributeLocation(attribute) ?? type.Locations.FirstOrDefault();
                if (BehaviorShapes.Interfaces(type, symbols).Length != 1)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Descriptors.MisplacedBehaviorAttribute,
                            location,
                            $"[GlobalBehavior] on '{typeName}' has no effect, because it does not implement exactly one Keryx behavior interface closing"
                        )
                    );
                }
                else if (isHost && TypeAccessibility.OfDeclaration(type) == AccessLevel.Restricted)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Descriptors.NotEffectivelyPublic,
                            location,
                            typeName,
                            Descriptors.AtLeastInternal,
                            "it is a global behavior and the host's generated mediator and AddKeryx() use it from outside its containing type"
                        )
                    );
                }
            }
            else if (KeryxSymbols.IsAttribute(attribute, symbols.BehaviorAttribute))
            {
                var messageType = MessageType.Classify(type, symbols);
                if (messageType is { IsNotificationOnly: true, IsConcrete: true, IsUnsupportedShape: false })
                {
                    CheckAttached(context.ReportDiagnostic, attribute, new(BehaviorTargetKind.Notification, [type]), typeName, symbols, isHost, checkConstraints: true);
                }
                else
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Descriptors.MisplacedBehaviorAttribute,
                            HandlerAnalysis.AttributeLocation(attribute) ?? type.Locations.FirstOrDefault(),
                            $"[Behavior] on '{typeName}' has no effect, because it is not a notification type; attach request and stream behaviors to the handler method"
                        )
                    );
                }
            }
        }
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context, KeryxSymbols symbols, bool isHost)
    {
        var method = (IMethodSymbol)context.Symbol;
        var attributes = method.GetAttributes().Where(a => KeryxSymbols.IsAttribute(a, symbols.BehaviorAttribute)).ToArray();
        if (attributes.Length == 0)
        {
            return;
        }

        var methodName = method.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
        if (!HandlerAnalysis.IsCandidate(method, symbols))
        {
            foreach (var attribute in attributes)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        Descriptors.MisplacedBehaviorAttribute,
                        HandlerAnalysis.AttributeLocation(attribute) ?? method.Locations.FirstOrDefault(),
                        $"[Behavior] on '{methodName}' has no effect, because it is not a handler"
                    )
                );
            }

            return;
        }

        // A handler with no single message parameter is KRX003; its behaviors cannot be matched against anything.
        var messages = method.Parameters.Select(p => MessageType.Classify(p.Type, symbols)).Where(static m => m.IsMessage).ToArray();
        if (messages is not [var message] || message.IsConflicting || message.IsUnsupportedShape)
        {
            return;
        }

        (BehaviorTarget Target, bool CheckConstraints)? target = message switch
        {
            { IsRequest: true, ResponseType: { } response } => (new(BehaviorTargetKind.Request, [message.Type, response]), true),
            { IsStream: true, ItemType: { } item } => (new(BehaviorTargetKind.Stream, [message.Type, item]), true),
            // A polymorphic notification handler's behaviors are closed over each concrete published type by the host (Phase 6); here
            // only the kind can be checked.
            { IsNotificationOnly: true } => (new(BehaviorTargetKind.NotificationHandler, [message.Type]), message.IsConcrete),
            _ => null,
        };
        if (target is not { } fit)
        {
            return;
        }

        foreach (var attribute in attributes)
        {
            CheckAttached(context.ReportDiagnostic, attribute, fit.Target, methodName, symbols, isHost, fit.CheckConstraints);
        }
    }

    /// <summary>KRX009 (and, in a host, KRX016) for one <c>[Behavior]</c> on a handler method or notification type.</summary>
    private static void CheckAttached(
        Action<Diagnostic> report,
        AttributeData attribute,
        BehaviorTarget target,
        string targetName,
        KeryxSymbols symbols,
        bool isHost,
        bool checkConstraints
    )
    {
        if (attribute.ConstructorArguments is not [{ Kind: TypedConstantKind.Type, Value: ITypeSymbol type }] || type.TypeKind == TypeKind.Error)
        {
            return;
        }

        var location = HandlerAnalysis.AttributeLocation(attribute);
        var behaviorName = type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
        if (type is not INamedTypeSymbol named || BehaviorShapes.Interfaces(named.OriginalDefinition, symbols).IsEmpty)
        {
            report(Diagnostic.Create(Descriptors.BehaviorDoesNotFit, location, behaviorName, targetName, "it is not a Keryx behavior"));
            return;
        }

        if (isHost && TypeAccessibility.Of(named.IsUnboundGenericType ? named.OriginalDefinition : named) == AccessLevel.Restricted)
        {
            report(
                Diagnostic.Create(
                    Descriptors.NotEffectivelyPublic,
                    location,
                    behaviorName,
                    Descriptors.AtLeastInternal,
                    $"'{targetName}' attaches it and the host's generated mediator and AddKeryx() use it from outside its containing type"
                )
            );
        }

        // A behavior class the shape rules reject is reported on the class (KRX019, KRX020).
        if (BehaviorShapes.Analyze(named.OriginalDefinition, symbols, new IssueCollector()) is not { } shape)
        {
            return;
        }

        var fits = checkConstraints
            ? BehaviorMatching.TryClose(named, shape, target, symbols.Compilation, out _, out var reason)
            : FitsKind(shape, target, out reason);
        if (!fits)
        {
            report(Diagnostic.Create(Descriptors.BehaviorDoesNotFit, location, behaviorName, targetName, reason));
        }

        static bool FitsKind(BehaviorShape shape, BehaviorTarget target, out string reason)
        {
            reason = $"it does not implement {target.ExpectedInterfaces}";
            return target.Accepts(shape.Kind);
        }
    }

    /// <summary>
    ///     KRX008 for behaviors declared in this compilation's source (ADR 0001, Ordering): an edge that contradicts phase or order, checked
    ///     once per behavior pair, and every cycle of edges between behaviors with the same phase and order, reported once. Edges to
    ///     behaviors in referenced assemblies are the host's (it checks them in the pipelines it composes); edges to types that are not
    ///     behaviors are ignored. Edges are compared per behavior class: an edge to one closing of a generic behavior counts as an edge to the
    ///     class (open-questions, behavior edge cases).
    /// </summary>
    private static void AnalyzeOrdering(CompilationAnalysisContext context, KeryxSymbols symbols, ConcurrentBag<INamedTypeSymbol> collected)
    {
        var behaviors = collected
                       .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
                       .Where(static b => b.Locations.Any(static l => l.IsInSource))
                       .OrderBy(static b => b.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                       .ToArray();
        if (behaviors.Length == 0)
        {
            return;
        }

        var index = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);
        for (var i = 0; i < behaviors.Length; i++)
        {
            index[behaviors[i]] = i;
        }

        var positions = behaviors.Select(b => BehaviorOrdering.ReadPosition(b, symbols)).ToArray();
        var graph = new List<(int To, BehaviorEdge Edge)>[behaviors.Length];
        var reportedPairs = new HashSet<(int Outer, int Inner)>();
        for (var i = 0; i < behaviors.Length; i++)
        {
            graph[i] = [];
        }

        for (var i = 0; i < behaviors.Length; i++)
        {
            foreach (var edge in BehaviorOrdering.ReadEdges(behaviors[i], symbols))
            {
                if (edge.Target is not INamedTypeSymbol target || !index.TryGetValue(target.OriginalDefinition, out var j))
                {
                    continue;
                }

                // Normalised to (outer, inner): RunsAfter X puts X outside, RunsBefore X puts X inside.
                var (outer, inner) = edge.RunsAfter ? (j, i) : (i, j);
                var name = behaviors[i].ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
                var targetName = target.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
                var location = HandlerAnalysis.AttributeLocation(edge.Attribute) ?? behaviors[i].Locations.FirstOrDefault();
                if (i == j)
                {
                    if (reportedPairs.Add((i, i)))
                    {
                        context.ReportDiagnostic(
                            Diagnostic.Create(
                                Descriptors.InvalidBehaviorOrdering,
                                location,
                                name,
                                $"declares {( edge.RunsAfter ? "RunsAfter" : "RunsBefore" )} itself, a cycle no order satisfies"
                            )
                        );
                    }

                    continue;
                }

                var comparison = Compare(positions[outer], positions[inner]);
                if (comparison > 0)
                {
                    if (reportedPairs.Add((outer, inner)))
                    {
                        context.ReportDiagnostic(
                            Diagnostic.Create(
                                Descriptors.InvalidBehaviorOrdering,
                                location,
                                name,
                                BehaviorOrderingText.Contradiction(edge.RunsAfter, targetName, positions[i], positions[j])
                            )
                        );
                    }
                }
                else if (comparison == 0)
                {
                    graph[outer].Add((inner, edge));
                }
            }
        }

        foreach (var component in BehaviorOrdering.StronglyConnectedComponents(graph.Length, node => graph[node].Select(static e => e.To)))
        {
            if (component.Count < 2)
            {
                continue;
            }

            component.Sort();
            var names = new List<string>(component.Count);
            foreach (var member in component)
            {
                names.Add($"'{behaviors[member].ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)}'");
            }

            var members = string.Join(", ", names);
            var first = component[0];
            Location? location = null;
            foreach (var (to, edge) in graph[first])
            {
                if (component.Contains(to))
                {
                    location = HandlerAnalysis.AttributeLocation(edge.Attribute);
                    break;
                }
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    Descriptors.InvalidBehaviorOrdering,
                    location ?? behaviors[first].Locations.FirstOrDefault(),
                    behaviors[first].ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                    $"is part of a RunsAfter/RunsBefore cycle between {members}, so no order satisfies them"
                )
            );
        }
    }

    private static int Compare((int Phase, int Order) left, (int Phase, int Order) right)
    {
        var byPhase = left.Phase.CompareTo(right.Phase);
        return byPhase != 0 ? byPhase : left.Order.CompareTo(right.Order);
    }
}
