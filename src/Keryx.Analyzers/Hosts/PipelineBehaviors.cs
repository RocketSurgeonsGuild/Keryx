using System.Collections.Concurrent;
using System.Collections.Immutable;
using Keryx.Analyzers.Behaviors;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     The behaviors and notification types a host composes its pipelines from, beyond those attached to a handler: <c>[assembly: Behavior]</c>
///     entries, <c>[GlobalBehavior]</c> classes (from the host's source and from its references), the host's <c>[assembly: ExcludeBehavior]</c>
///     entries, and every concrete notification type the host knows. Built once per composition and shared by every handler. Holds symbols,
///     so it never reaches an incremental model.
/// </summary>
internal sealed class BehaviorCatalog
{
    /// <summary>The value of <c>PublishStrategy.Parallel</c>.</summary>
    private const int ParallelStrategy = 1;

    private readonly ConcurrentDictionary<INamedTypeSymbol, BehaviorShape?> _shapes = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<INamedTypeSymbol, HostNotificationModel?> _notifications = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IAssemblySymbol, ImmutableArray<ITypeSymbol>> _assemblyBehaviors = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<INamedTypeSymbol, ITypeSymbol?> _constructorErrors = new(SymbolEqualityComparer.Default);

    private readonly IReadOnlyDictionary<string, ImmutableArray<ITypeSymbol>> _referencedAssemblyBehaviors;

    private BehaviorCatalog(
        Compilation compilation,
        KeryxSymbols symbols,
        IEnumerable<INamedTypeSymbol> referencedGlobals,
        IReadOnlyDictionary<string, ImmutableArray<ITypeSymbol>> referencedAssemblyBehaviors
    )
    {
        Compilation = compilation;
        Symbols = symbols;
        _referencedAssemblyBehaviors = referencedAssemblyBehaviors;
        var assemblyBehaviors = ImmutableArray.CreateBuilder<ITypeSymbol>();
        var exclusions = ImmutableArray.CreateBuilder<(ITypeSymbol, AttributeData)>();
        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (attribute.ConstructorArguments is not [{ Kind: TypedConstantKind.Type, Value: ITypeSymbol type }])
            {
                continue;
            }

            if (KeryxSymbols.IsAttribute(attribute, symbols.BehaviorAttribute))
            {
                assemblyBehaviors.Add(type);
            }
            else if (KeryxSymbols.IsAttribute(attribute, symbols.ExcludeBehaviorAttribute))
            {
                exclusions.Add((type, attribute));
            }
        }

        AssemblyBehaviors = assemblyBehaviors.ToImmutable();
        Exclusions = exclusions.ToImmutable();

        var globals = ImmutableArray.CreateBuilder<(INamedTypeSymbol, AttributeData)>();
        if (symbols.GlobalBehaviorAttribute is { } globalBehaviorAttribute)
        {
            CollectGlobals(compilation.Assembly.GlobalNamespace, globalBehaviorAttribute, globals);
            foreach (var reference in referencedGlobals)
            {
                // The scan names a generic global behavior unbound; the catalog holds definitions, as for the ones in source.
                var type = reference.OriginalDefinition;
                if (type.GetAttributes().FirstOrDefault(a => KeryxSymbols.IsAttribute(a, globalBehaviorAttribute)) is { } attribute)
                {
                    globals.Add((type, attribute));
                }
            }
        }

        GlobalBehaviors = globals.ToImmutable();
    }

    public Compilation Compilation { get; }
    public KeryxSymbols Symbols { get; }

    /// <summary>The <c>[assembly: Behavior(typeof(X))]</c> types, in declaration order.</summary>
    public ImmutableArray<ITypeSymbol> AssemblyBehaviors { get; }

    /// <summary>
    ///     The <c>[GlobalBehavior]</c> classes: those declared in this compilation (in namespace and declaration order), then the referenced
    ///     ones (in assembly and metadata name order), each with its attribute.
    /// </summary>
    public ImmutableArray<(INamedTypeSymbol Type, AttributeData Attribute)> GlobalBehaviors { get; }

    /// <summary>The <c>[assembly: ExcludeBehavior(typeof(X))]</c> entries.</summary>
    public ImmutableArray<(ITypeSymbol Type, AttributeData Attribute)> Exclusions { get; }

    /// <summary>
    ///     Every concrete notification type the host knows (ADR 0001, Polymorphic handlers; OQ-42): the ones declared in its source, the
    ///     concrete notification parameter types of its handlers and of its referenced handlers, and the notification types of its references
    ///     (from the scan). The same set for every handler, so a type known through one handler reaches the polymorphic handlers too.
    /// </summary>
    public ImmutableArray<INamedTypeSymbol> KnownNotifications { get; private set; } = ImmutableArray<INamedTypeSymbol>.Empty;

    /// <summary>A catalog for one composition of a host.</summary>
    /// <param name="compilation">The host's compilation.</param>
    /// <param name="symbols">Its Keryx symbols.</param>
    /// <param name="referencedGlobals">The <c>[GlobalBehavior]</c> classes of its references (unbound when generic).</param>
    /// <param name="referencedAssemblyBehaviors">
    ///     The <c>[assembly: Behavior]</c> types of its references, by assembly name, from their projections; an assembly not listed is read
    ///     from metadata.
    /// </param>
    public static BehaviorCatalog Create(
        Compilation compilation,
        KeryxSymbols symbols,
        IEnumerable<INamedTypeSymbol> referencedGlobals,
        IReadOnlyDictionary<string, ImmutableArray<ITypeSymbol>> referencedAssemblyBehaviors
    ) =>
        new(compilation, symbols, referencedGlobals, referencedAssemblyBehaviors);

    /// <summary>
    ///     Sets <see cref="KnownNotifications" />: each type once, in the order given, keeping only the types <see cref="IsKnowable" /> accepts.
    ///     Call it before composing any notification handler.
    /// </summary>
    public void SetKnownNotifications(IEnumerable<INamedTypeSymbol> notifications)
    {
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        KnownNotifications = [.. notifications.Where(type => seen.Add(type) && IsKnowable(type))];
    }

    /// <summary>
    ///     The <c>[assembly: Behavior(typeof(X))]</c> types of an assembly, in declaration order: <see cref="AssemblyBehaviors" /> for this
    ///     compilation's own assembly, the projected ones for a scanned reference, read from metadata for any other.
    /// </summary>
    public ImmutableArray<ITypeSymbol> AssemblyBehaviorsOf(IAssemblySymbol? assembly) =>
        assembly is null || SymbolEqualityComparer.Default.Equals(assembly, Compilation.Assembly)
            ? AssemblyBehaviors
            : _referencedAssemblyBehaviors.TryGetValue(assembly.MetadataName, out var projected)
                ? projected
                : _assemblyBehaviors.GetOrAdd(assembly, a => [.. ReadAssemblyBehaviors(a, Symbols)]);

    /// <summary>
    ///     The host's model of a concrete notification type (ADR 0001, Notifications): its publish strategy and its per-publish behaviors, or
    ///     <see langword="null" /> when the host cannot know the type (see <see cref="IsKnowable" />). Built once per type.
    /// </summary>
    public HostNotificationModel? Notification(INamedTypeSymbol type) => _notifications.GetOrAdd(type, CreateNotification);

    /// <summary>
    ///     Whether the host can know a notification type: a concrete notification (KRX011, KRX012 shapes excluded) that generated code can
    ///     reach: at least <c>internal</c> in the host's source (OQ-42, OQ-44), accessible from the host when it is referenced.
    /// </summary>
    public bool IsKnowable(INamedTypeSymbol type)
    {
        var messageType = MessageType.Classify(type, Symbols);
        return messageType is { IsNotificationOnly: true, IsConcrete: true, IsUnsupportedShape: false }
         && type.TypeKind != TypeKind.Error
         && TypeAccessibility.Of(type) != AccessLevel.Restricted
         && Compilation.IsSymbolAccessibleWithin(type, Compilation.Assembly);
    }

    /// <summary>
    ///     Whether a notification of type <paramref name="notification" /> reaches a handler whose parameter is
    ///     <paramref name="parameterType" />: an identity, implicit reference or boxing conversion (ADR 0001, Polymorphic handlers).
    /// </summary>
    public static bool Converts(ITypeSymbol notification, ITypeSymbol parameterType, Compilation compilation)
    {
        if (compilation is not Microsoft.CodeAnalysis.CSharp.CSharpCompilation csharp)
        {
            return false;
        }

        var conversion = csharp.ClassifyConversion(notification, parameterType);
        return conversion.IsIdentity || ( conversion.IsImplicit && conversion.IsReference ) || conversion.IsBoxing;
    }

    private HostNotificationModel? CreateNotification(INamedTypeSymbol type)
    {
        if (!IsKnowable(type))
        {
            return null;
        }

        var parallel = false;
        var attached = ImmutableArray.CreateBuilder<ITypeSymbol>();
        foreach (var attribute in type.GetAttributes())
        {
            if (KeryxSymbols.IsAttribute(attribute, Symbols.PublishStrategyAttribute) && attribute.ConstructorArguments is [{ Value: int strategy }])
            {
                parallel = strategy == ParallelStrategy;
            }
            else if (KeryxSymbols.IsAttribute(attribute, Symbols.BehaviorAttribute)
                  && attribute.ConstructorArguments is [{ Kind: TypedConstantKind.Type, Value: ITypeSymbol behavior }])
            {
                attached.Add(behavior);
            }
        }

        var (behaviors, usedExclusions) = PipelineBehaviors.Compose(
            attached,
            AssemblyBehaviorsOf(type.ContainingAssembly),
            new(BehaviorTargetKind.Notification, [type]),
            this
        );

        return new(
            CSharpText.TypeOf(type),
            type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
            type.IsValueType,
            type.IsValueType || type.IsSealed,
            parallel,
            behaviors,
            usedExclusions,
            LocationInfo.From(type.Locations.FirstOrDefault())
        );
    }

    private static IEnumerable<ITypeSymbol> ReadAssemblyBehaviors(IAssemblySymbol assembly, KeryxSymbols symbols)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            if (KeryxSymbols.IsAttribute(attribute, symbols.BehaviorAttribute)
             && attribute.ConstructorArguments is [{ Kind: TypedConstantKind.Type, Value: ITypeSymbol type }])
            {
                yield return type;
            }
        }
    }

    /// <summary>
    ///     Whether generated code can construct a behavior (its definition): <see langword="false" /> for a behavior from another assembly with
    ///     a public constructor parameter type the host cannot resolve (OQ-47). Those are listed in <see cref="UnresolvedConstructors" />.
    /// </summary>
    public bool CanConstruct(INamedTypeSymbol definition) =>
        SymbolEqualityComparer.Default.Equals(definition.ContainingAssembly, Compilation.Assembly)
     || _constructorErrors.GetOrAdd(definition, static d => HostComposer.ConstructorError(d)) is null;

    /// <summary>
    ///     The behaviors <see cref="CanConstruct" /> rejected so far, each with the first type that does not resolve, in name order, for
    ///     KRX034.
    /// </summary>
    public IEnumerable<(INamedTypeSymbol Behavior, ITypeSymbol Error)> UnresolvedConstructors =>
        _constructorErrors
           .Where(static pair => pair.Value is not null)
           .Select(static pair => (pair.Key, pair.Value!))
           .OrderBy(static pair => pair.Key.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal);

    /// <summary>The shape of a behavior class (its definition), or <see langword="null" /> when the behavior rules reject it.</summary>
    public BehaviorShape? Shape(INamedTypeSymbol type) =>
        _shapes.GetOrAdd(type.OriginalDefinition, definition => BehaviorShapes.Analyze(definition, Symbols, new IssueCollector()));

    private static void CollectGlobals(INamespaceSymbol @namespace, INamedTypeSymbol attributeType, ImmutableArray<(INamedTypeSymbol, AttributeData)>.Builder globals)
    {
        foreach (var member in @namespace.GetMembers())
        {
            if (member is INamespaceSymbol nested)
            {
                CollectGlobals(nested, attributeType, globals);
            }
            else if (member is INamedTypeSymbol type)
            {
                CollectGlobals(type, attributeType, globals);
            }
        }
    }

    private static void CollectGlobals(INamedTypeSymbol type, INamedTypeSymbol attributeType, ImmutableArray<(INamedTypeSymbol, AttributeData)>.Builder globals)
    {
        if (type.GetAttributes().FirstOrDefault(a => KeryxSymbols.IsAttribute(a, attributeType)) is { } attribute)
        {
            globals.Add((type, attribute));
        }

        foreach (var nested in type.GetTypeMembers())
        {
            CollectGlobals(nested, attributeType, globals);
        }
    }
}

/// <summary>
///     Composes the behaviors of one pipeline from the three scopes (ADR 0001, Three ways to attach a behavior) and projects them into
///     <see cref="PipelineBehaviorModel" />s.
/// </summary>
internal static class PipelineBehaviors
{
    /// <summary>
    ///     The closed behaviors that apply to a handler: the behaviors attached to the handler method (read from its <c>[Behavior]</c>s for one
    ///     of the host's own handlers, from <c>KeryxHandler.Behaviors</c> for a referenced one), then its assembly's, then the global ones,
    ///     each closed over the target and kept when it fits; de-duplicated by closed type; minus the host's exclusions. A behavior that does
    ///     not fit is skipped (KRX009 for one attached to the method: the analyzer reports it for the host's own handlers, the host for
    ///     referenced ones), as is one the behavior rules reject (KRX019, KRX020), one generated code cannot reach (narrower than
    ///     <c>internal</c>) or one whose constructor names a type the host cannot resolve (KRX034).
    /// </summary>
    public static (ImmutableArray<PipelineBehaviorModel> Behaviors, ImmutableArray<string> UsedExclusions) For(
        ImmutableArray<ITypeSymbol> attached,
        IAssemblySymbol? handlerAssembly,
        BehaviorTarget target,
        BehaviorCatalog catalog
    ) =>
        Compose(attached, catalog.AssemblyBehaviorsOf(handlerAssembly), target, catalog);

    /// <summary>
    ///     The closed behaviors that apply to one target: <paramref name="attached" /> (per target), then <paramref name="assembly" /> (per
    ///     assembly), then the global ones, each closed over the target and kept when it fits; de-duplicated by closed type; minus the host's
    ///     exclusions.
    /// </summary>
    public static (ImmutableArray<PipelineBehaviorModel> Behaviors, ImmutableArray<string> UsedExclusions) Compose(
        IEnumerable<ITypeSymbol> attached,
        IEnumerable<ITypeSymbol> assembly,
        BehaviorTarget target,
        BehaviorCatalog catalog
    )
    {
        var behaviors = new SortedDictionary<string, PipelineBehaviorModel>(StringComparer.Ordinal);
        var usedExclusions = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var type in attached)
        {
            Apply(type);
        }

        foreach (var type in assembly)
        {
            Apply(type);
        }

        foreach (var (type, _) in catalog.GlobalBehaviors)
        {
            Apply(type.IsGenericType ? type.ConstructUnboundGenericType() : type);
        }

        return ([.. behaviors.Values], [.. usedExclusions]);

        void Apply(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol named
             || catalog.Shape(named) is not { } shape
             || !catalog.CanConstruct(shape.Type)
             || !BehaviorMatching.TryClose(named, shape, target, catalog.Compilation, out var closed, out _)
             || TypeAccessibility.Of(closed) == AccessLevel.Restricted)
            {
                return;
            }

            var identity = CSharpText.TypeOf(closed);
            var definition = BehaviorOrdering.DefinitionOperand(shape.Type);
            foreach (var (excluded, _) in catalog.Exclusions)
            {
                var operand = BehaviorOrdering.Operand(excluded);
                if (string.Equals(operand, excluded is INamedTypeSymbol { IsUnboundGenericType: true } ? definition : identity, StringComparison.Ordinal))
                {
                    usedExclusions.Add(operand);
                    return;
                }
            }

            if (!behaviors.ContainsKey(identity))
            {
                behaviors.Add(identity, Model(closed, shape, catalog));
            }
        }
    }

    /// <summary>
    ///     The target of a request or stream request handler, or <see langword="null" /> for a notification handler, whose per-handler
    ///     behaviors are closed over each concrete type it receives (see <c>HostModelFactory.Deliveries</c>).
    /// </summary>
    public static BehaviorTarget? Target(HandlerKind kind, ITypeSymbol message, ITypeSymbol? response) =>
        (kind, response) switch
        {
            (HandlerKind.Request, { } responseType) => new BehaviorTarget(BehaviorTargetKind.Request, [message, responseType]),
            (HandlerKind.Stream, { } item) => new BehaviorTarget(BehaviorTargetKind.Stream, [message, item]),
            _ => null,
        };

    private static PipelineBehaviorModel Model(INamedTypeSymbol closed, BehaviorShape shape, BehaviorCatalog catalog)
    {
        var symbols = catalog.Symbols;
        var (phase, order) = BehaviorOrdering.ReadPosition(shape.Type, symbols);
        var edges = BehaviorOrdering
                   .ReadEdges(shape.Type, symbols)
                   .Select(static e => new BehaviorEdgeModel(
                               new(e.RunsAfter, BehaviorOrdering.Operand(e.Target), e.TargetIsOpen),
                               LocationInfo.From(HandlerAnalysis.AttributeLocation(e.Attribute))
                           )
                    )
                   .ToImmutableArray();

        // The closed type's constructor has its parameter types substituted (ILogger<TRequest> becomes ILogger<Ping>).
        var constructor = closed.InstanceConstructors.First(static c => c.DeclaredAccessibility == Accessibility.Public);
        var services = constructor
                      .Parameters.Select(static p => HostModelFactory.Service(p.Type, HandlerModelFactory.ServiceKey(p), CSharpText.DefaultValue(p)))
                      .ToImmutableArray();

        return new(
            CSharpText.Type(closed),
            CSharpText.TypeOf(closed),
            BehaviorOrdering.DefinitionOperand(shape.Type),
            shape.Kind,
            phase,
            order,
            edges,
            services,
            shape.Type.ContainingAssembly?.Name ?? "",
            SymbolEqualityComparer.Default.Equals(shape.Type.ContainingAssembly, catalog.Compilation.Assembly)
        );
    }
}
