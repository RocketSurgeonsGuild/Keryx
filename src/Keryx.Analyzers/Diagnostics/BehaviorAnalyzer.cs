using System.Collections.Concurrent;
using System.Collections.Immutable;
using Keryx.Analyzers.Behaviors;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Keryx.Analyzers.Diagnostics;

/// <summary>
///     The rules on behaviors (ADR 0001, Behaviors, Ordering): KRX019 (and KRX013 and KRX018 on behavior method parameters) on behavior
///     classes, KRX009 for a behavior attached to a request or stream handler method or a notification type that does not fit it, KRX042 for
///     <c>[Behavior]</c> or <c>[GlobalBehavior]</c> where it does nothing, KRX016 for a behavior generated code cannot reach, and, at
///     compilation end, KRX008 for <c>RunsAfter</c>/<c>RunsBefore</c> edges between behaviors declared in this compilation's source.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BehaviorAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        Descriptors.InvalidBehaviorOrdering,
        Descriptors.BehaviorDoesNotFit,
        Descriptors.InvalidBehaviorParameterKind,
        Descriptors.NotEffectivelyPublic,
        Descriptors.InvalidServiceKey,
        Descriptors.InvalidBehaviorShape,
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
                startContext.RegisterSymbolAction(symbolContext => AnalyzeMethod(symbolContext, symbols, isHost), SymbolKind.Method);
                startContext.RegisterCompilationEndAction(endContext => AnalyzeOrdering(endContext, symbols, behaviors));
                if (isHost)
                {
                    // Assembly attributes have no symbol action; each KRX016 is reported from the syntax tree that declares the attribute.
                    var assemblyReach = AssemblyBehaviorReach(startContext.Compilation, symbols);
                    if (!assemblyReach.IsEmpty)
                    {
                        startContext.RegisterSyntaxTreeAction(
                            treeContext =>
                            {
                                foreach (var diagnostic in assemblyReach)
                                {
                                    if (diagnostic.Location.SourceTree == treeContext.Tree)
                                    {
                                        treeContext.ReportDiagnostic(diagnostic);
                                    }
                                }
                            }
                        );
                    }
                }
            }
        );
    }

    /// <summary>
    ///     KRX016 for each of a host's own <c>[assembly: Behavior(typeof(X))]</c> entries whose behavior class, or one of its behavior methods,
    ///     is narrower than <c>internal</c>: the host would skip it silently. Outside a host, the message type rules report the entries that
    ///     are not public.
    /// </summary>
    private static ImmutableArray<Diagnostic> AssemblyBehaviorReach(Compilation compilation, KeryxSymbols symbols)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (!KeryxSymbols.IsAttribute(attribute, symbols.BehaviorAttribute)
             || attribute.ConstructorArguments is not [{ Kind: TypedConstantKind.Type, Value: INamedTypeSymbol type }]
             || type.TypeKind == TypeKind.Error
             || !BehaviorShapes.IsBehaviorClass(type, symbols)
             || HandlerAnalysis.AttributeLocation(attribute) is not { SourceTree: not null } location)
            {
                continue;
            }

            foreach (var method in BehaviorMethods(type, symbols))
            {
                if (ReportReach(diagnostics.Add, location, type, method, isHost: true, "this assembly attaches it to its handlers"))
                {
                    break;
                }
            }
        }

        return diagnostics.ToImmutable();
    }

    private static void AnalyzeType(SymbolAnalysisContext context, KeryxSymbols symbols, bool isHost, ConcurrentBag<INamedTypeSymbol> behaviors)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        var typeName = type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
        var isBehavior = BehaviorShapes.IsBehaviorClass(type, symbols);
        if (isBehavior)
        {
            behaviors.Add(type);
            var issues = new IssueCollector();
            BehaviorShapes.Analyze(type, symbols, issues);
            issues.ReportTo(context.ReportDiagnostic, isHost);
        }

        foreach (var attribute in type.GetAttributes())
        {
            var location = HandlerAnalysis.AttributeLocation(attribute) ?? type.Locations.FirstOrDefault();
            if (KeryxSymbols.IsAttribute(attribute, symbols.GlobalBehaviorAttribute))
            {
                if (!isBehavior)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Descriptors.MisplacedBehaviorAttribute,
                            location,
                            $"[GlobalBehavior] on '{typeName}' has no effect, because it carries no behavior kind attribute ([RequestBehavior], [StreamBehavior] or [NotificationBehavior])"
                        )
                    );
                    continue;
                }

                // Every behavior method of the class is global, so each must be reachable from the host's generated code. Outside a host,
                // the message type rules report a global behavior that is not public.
                if (isHost)
                {
                    foreach (var method in BehaviorMethods(type, symbols))
                    {
                        if (ReportReach(context.ReportDiagnostic, location, type, method, isHost, "it is a global behavior"))
                        {
                            break;
                        }
                    }
                }
            }
            else if (KeryxSymbols.IsAttribute(attribute, symbols.BehaviorAttribute))
            {
                var messageType = MessageType.Classify(type, symbols);
                if (messageType is { IsNotificationOnly: true, IsConcrete: true, IsUnsupportedShape: false })
                {
                    CheckAttached(context.ReportDiagnostic, attribute, new(BehaviorKind.Notification, type, null), typeName, symbols, isHost);
                }
                else
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Descriptors.MisplacedBehaviorAttribute,
                            location,
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
        var methodName = method.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
        foreach (var attribute in method.GetAttributes())
        {
            if (!KeryxSymbols.IsAttribute(attribute, symbols.GlobalBehaviorAttribute))
            {
                continue;
            }

            var location = HandlerAnalysis.AttributeLocation(attribute) ?? method.Locations.FirstOrDefault();
            if (BehaviorShapes.BehaviorMethodKind(method, symbols) is null)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        Descriptors.MisplacedBehaviorAttribute,
                        location,
                        $"[GlobalBehavior] on '{methodName}' has no effect, because it is not a behavior method (a static Handle or HandleAsync of a kind its class carries)"
                    )
                );
            }
            else if (isHost)
            {
                ReportReach(context.ReportDiagnostic, location, method.ContainingType, method, isHost, "it is a global behavior");
            }
        }

        var attributes = method.GetAttributes().Where(a => KeryxSymbols.IsAttribute(a, symbols.BehaviorAttribute)).ToArray();
        if (attributes.Length == 0)
        {
            return;
        }

        if (!HandlerAnalysis.IsCandidate(method, symbols))
        {
            Misplaced(context, attributes, method, $"[Behavior] on '{methodName}' has no effect, because it is not a handler");
            return;
        }

        // A handler with no single message parameter is KRX003; its behaviors cannot be matched against anything.
        var messages = method.Parameters.Select(p => MessageType.Classify(p.Type, symbols)).Where(static m => m.IsMessage).ToArray();
        if (messages is not [var message] || message.IsConflicting || message.IsUnsupportedShape)
        {
            return;
        }

        BehaviorTarget? target = message switch
        {
            { IsRequest: true, ResponseType: { } response } => new(BehaviorKind.Request, message.Type, response),
            { IsStream: true, ItemType: { } item } => new(BehaviorKind.Stream, message.Type, item),
            _ => null,
        };
        if (target is not { } fit)
        {
            if (message.IsNotificationOnly)
            {
                // A notification handler has no pipeline of its own: notification behaviors wrap the whole publish (ADR 0001, Notifications).
                Misplaced(
                    context,
                    attributes,
                    method,
                    $"[Behavior] on '{methodName}' has no effect, because it is a notification handler; notification behaviors wrap the whole publish, so attach them to the notification type"
                );
            }

            return;
        }

        foreach (var attribute in attributes)
        {
            CheckAttached(context.ReportDiagnostic, attribute, fit, methodName, symbols, isHost);
        }

        static void Misplaced(SymbolAnalysisContext context, AttributeData[] attributes, IMethodSymbol method, string message)
        {
            foreach (var attribute in attributes)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Descriptors.MisplacedBehaviorAttribute, HandlerAnalysis.AttributeLocation(attribute) ?? method.Locations.FirstOrDefault(), message)
                );
            }
        }
    }

    /// <summary>
    ///     KRX009 (and, in a host, KRX016) for one <c>[Behavior]</c> on a request or stream handler method or a notification type. Outside a
    ///     host, the handler and message type rules report a behavior that is not public.
    /// </summary>
    private static void CheckAttached(
        Action<Diagnostic> report,
        AttributeData attribute,
        BehaviorTarget target,
        string targetName,
        KeryxSymbols symbols,
        bool isHost
    )
    {
        if (attribute.ConstructorArguments is not [{ Kind: TypedConstantKind.Type, Value: ITypeSymbol type }] || type.TypeKind == TypeKind.Error)
        {
            return;
        }

        var location = HandlerAnalysis.AttributeLocation(attribute);
        var behaviorName = type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
        if (type is not INamedTypeSymbol named || !BehaviorShapes.IsBehaviorClass(named, symbols))
        {
            report(Diagnostic.Create(Descriptors.BehaviorDoesNotFit, location, behaviorName, targetName, "it is not a behavior class (a static class with [RequestBehavior], [StreamBehavior] or [NotificationBehavior])"));
            return;
        }

        // A behavior class the shape rules reject is reported on the class (KRX019).
        if (BehaviorShapes.Analyze(named, symbols, new IssueCollector()) is not { } shape)
        {
            return;
        }

        if (!BehaviorMatching.TryMatch(shape, target, symbols.Compilation, out var match, out var reason))
        {
            report(Diagnostic.Create(Descriptors.BehaviorDoesNotFit, location, behaviorName, targetName, reason));
            return;
        }

        if (isHost)
        {
            ReportReach(report, location, named, match.Method.Method, isHost, $"'{targetName}' attaches it");
        }
    }

    /// <summary>
    ///     KRX016 for a behavior class or method generated code cannot reach: narrower than <c>internal</c> in a host, not <c>public</c>
    ///     outside one (a host reads it from this assembly's metadata). <see langword="true" /> when it reported.
    /// </summary>
    private static bool ReportReach(Action<Diagnostic> report, Location? location, INamedTypeSymbol type, IMethodSymbol method, bool isHost, string because)
    {
        var required = isHost ? AccessLevel.Internal : AccessLevel.Public;
        if (BehaviorShapes.NarrowerThan(type, method, required) is not { } narrow)
        {
            return false;
        }

        report(
            Diagnostic.Create(
                Descriptors.NotEffectivelyPublic,
                location,
                narrow,
                isHost ? Descriptors.AtLeastInternal : Descriptors.Public,
                isHost
                    ? $"{because} and the host's generated mediator calls it from outside its containing type"
                    : $"{because} and a host calls it from another assembly"
            )
        );
        return true;
    }

    /// <summary>The behavior methods of a class's carried kinds, whatever their shape, in declaration order.</summary>
    private static IEnumerable<IMethodSymbol> BehaviorMethods(INamedTypeSymbol type, KeryxSymbols symbols) =>
        BehaviorShapes.Candidates(type).Where(m => BehaviorShapes.BehaviorMethodKind(m, symbols) is not null);

    /// <summary>
    ///     KRX008 for behaviors declared in this compilation's source (ADR 0001, Ordering), checked per kind over the classes that declare a
    ///     behavior method of that kind: an edge that contradicts phase or order, and every cycle of edges between behaviors with the same
    ///     phase and order. Each pair (and cycle) of behavior classes is reported once, and once per kind where a behavior method overrides its
    ///     class's ordering. Edges to behaviors in referenced assemblies are the host's (it checks them in the pipelines it composes); edges to
    ///     classes that have no behavior method of the kind are ignored for that kind.
    /// </summary>
    private static void AnalyzeOrdering(CompilationAnalysisContext context, KeryxSymbols symbols, ConcurrentBag<INamedTypeSymbol> collected)
    {
        var classes = collected
                     .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
                     .Where(static b => b.Locations.Any(static l => l.IsInSource))
                     .OrderBy(static b => b.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                     .ToArray();
        if (classes.Length == 0)
        {
            return;
        }

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kind in (BehaviorKind[])[BehaviorKind.Request, BehaviorKind.Stream, BehaviorKind.Notification])
        {
            var nodes = new List<(INamedTypeSymbol Class, IMethodSymbol Method)>();
            foreach (var type in classes)
            {
                if (BehaviorShapes.CandidateOfKind(type, kind, symbols) is { } method)
                {
                    nodes.Add((type, method));
                }
            }

            AnalyzeOrdering(context, symbols, kind, nodes, reported);
        }
    }

    private static void AnalyzeOrdering(
        CompilationAnalysisContext context,
        KeryxSymbols symbols,
        BehaviorKind kind,
        List<(INamedTypeSymbol Class, IMethodSymbol Method)> nodes,
        HashSet<string> reported
    )
    {
        var index = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);
        for (var i = 0; i < nodes.Count; i++)
        {
            index[nodes[i].Class] = i;
        }

        var positions = nodes.Select(n => BehaviorOrdering.ReadPosition(n.Method, symbols)).ToArray();
        var overrides = nodes.Select(n => BehaviorOrdering.OverridesOrdering(n.Method, symbols)).ToArray();
        var kindName = BehaviorShapes.KindName(kind);
        var graph = new List<(int To, BehaviorEdge Edge)>[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            graph[i] = [];
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            foreach (var edge in BehaviorOrdering.ReadEdges(nodes[i].Method, symbols))
            {
                if (!index.TryGetValue(edge.Target, out var j))
                {
                    continue;
                }

                // Normalised to (outer, inner): RunsAfter X puts X outside, RunsBefore X puts X inside.
                var (outer, inner) = edge.RunsAfter ? (j, i) : (i, j);
                var perKind = overrides[i] || overrides[j] ? kindName : null;
                var name = nodes[i].Class.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
                var location = HandlerAnalysis.AttributeLocation(edge.Attribute) ?? nodes[i].Class.Locations.FirstOrDefault();
                if (i == j)
                {
                    if (reported.Add($"{Key(nodes[i].Class)}|itself|{perKind}"))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Descriptors.InvalidBehaviorOrdering, location, name, BehaviorOrderingText.Itself(edge.RunsAfter, perKind)));
                    }

                    continue;
                }

                var comparison = Compare(positions[outer], positions[inner]);
                if (comparison > 0)
                {
                    if (reported.Add($"{Key(nodes[outer].Class)}|{Key(nodes[inner].Class)}|{perKind}"))
                    {
                        context.ReportDiagnostic(
                            Diagnostic.Create(
                                Descriptors.InvalidBehaviorOrdering,
                                location,
                                name,
                                BehaviorOrderingText.Contradiction(
                                    edge.RunsAfter,
                                    edge.Target.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                                    positions[i],
                                    positions[j],
                                    perKind
                                )
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
            var perKind = AnyOverrides(component, overrides) ? kindName : null;
            if (!reported.Add("cycle|" + Join(component, nodes, "|", static t => Key(t)) + "|" + perKind))
            {
                continue;
            }

            var members = Join(component, nodes, ", ", static t => $"'{t.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)}'");
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
                    location ?? nodes[first].Class.Locations.FirstOrDefault(),
                    nodes[first].Class.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                    BehaviorOrderingText.Cycle(members, perKind)
                )
            );
        }

        static string Key(INamedTypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        static bool AnyOverrides(List<int> component, bool[] overrides)
        {
            foreach (var member in component)
            {
                if (overrides[member])
                {
                    return true;
                }
            }

            return false;
        }

        static string Join(List<int> component, List<(INamedTypeSymbol Class, IMethodSymbol Method)> nodes, string separator, Func<INamedTypeSymbol, string> text)
        {
            var parts = new List<string>(component.Count);
            foreach (var member in component)
            {
                parts.Add(text(nodes[member].Class));
            }

            return string.Join(separator, parts);
        }
    }

    private static int Compare((int Phase, int Order) left, (int Phase, int Order) right)
    {
        var byPhase = left.Phase.CompareTo(right.Phase);
        return byPhase != 0 ? byPhase : left.Order.CompareTo(right.Order);
    }
}
