using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Keryx.Analyzers.Behaviors;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     The behaviors a compilation offers to its host's pipelines beyond those attached to a handler method: <c>[assembly: Behavior]</c>
///     entries, <c>[GlobalBehavior]</c> classes in its source, and the host's <c>[assembly: ExcludeBehavior]</c> entries. Built once per
///     compilation and shared by every handler of it. Holds symbols, so it never reaches an incremental model.
/// </summary>
internal sealed class BehaviorCatalog
{
    private static readonly ConditionalWeakTable<Compilation, BehaviorCatalog> Catalogs = new();

    /// <summary>The value of <c>PublishStrategy.Parallel</c>.</summary>
    private const int ParallelStrategy = 1;

    private readonly ConcurrentDictionary<INamedTypeSymbol, BehaviorShape?> _shapes = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<INamedTypeSymbol, HostNotificationModel?> _notifications = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IAssemblySymbol, ImmutableArray<ITypeSymbol>> _assemblyBehaviors = new(SymbolEqualityComparer.Default);

    private BehaviorCatalog(Compilation compilation, KeryxSymbols symbols)
    {
        Compilation = compilation;
        Symbols = symbols;
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
        }

        GlobalBehaviors = globals.ToImmutable();
    }

    public Compilation Compilation { get; }
    public KeryxSymbols Symbols { get; }

    /// <summary>The <c>[assembly: Behavior(typeof(X))]</c> types, in declaration order.</summary>
    public ImmutableArray<ITypeSymbol> AssemblyBehaviors { get; }

    /// <summary>The <c>[GlobalBehavior]</c> classes declared in this compilation, with the attribute, in namespace and declaration order.</summary>
    public ImmutableArray<(INamedTypeSymbol Type, AttributeData Attribute)> GlobalBehaviors { get; }

    /// <summary>The <c>[assembly: ExcludeBehavior(typeof(X))]</c> entries.</summary>
    public ImmutableArray<(ITypeSymbol Type, AttributeData Attribute)> Exclusions { get; }

    /// <summary>
    ///     The concrete notification types declared in this compilation that generated code can reach (at least <c>internal</c>), in namespace
    ///     and declaration order. Built on first use.
    /// </summary>
    public ImmutableArray<INamedTypeSymbol> SourceNotifications
    {
        get
        {
            if (field.IsDefault)
            {
                var notifications = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
                CollectTypes(Compilation.Assembly.GlobalNamespace, type => Notification(type) is not null, notifications);
                ImmutableInterlocked.InterlockedInitialize(ref field, notifications.ToImmutable());
            }

            return field;
        }
    }

    /// <summary>
    ///     Every concrete notification type the host knows (ADR 0001, Polymorphic handlers; OQ-42): <see cref="SourceNotifications" />, then
    ///     the parameter type of each notification handler in this compilation's source that the host keeps, when that is a concrete
    ///     notification declared elsewhere (typically a contracts assembly). The same set for every handler, so a referenced type known
    ///     through one handler reaches the polymorphic handlers too. Built on first use.
    /// </summary>
    public ImmutableArray<INamedTypeSymbol> KnownNotifications
    {
        get
        {
            if (field.IsDefault)
            {
                var known = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
                known.AddRange(SourceNotifications);
                var seen = new HashSet<INamedTypeSymbol>(SourceNotifications, SymbolEqualityComparer.Default);
                var handlerTypes = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
                CollectTypes(Compilation.Assembly.GlobalNamespace, static _ => true, handlerTypes);
                foreach (var type in handlerTypes)
                {
                    foreach (var method in HandlerAnalysis.GetCandidates(type, Symbols))
                    {
                        if (HandledNotification(method) is { } notification && seen.Add(notification))
                        {
                            known.Add(notification);
                        }
                    }
                }

                ImmutableInterlocked.InterlockedInitialize(ref field, known.ToImmutable());
            }

            return field;
        }
    }

    public static BehaviorCatalog For(Compilation compilation, KeryxSymbols symbols) =>
        Catalogs.GetValue(compilation, c => new(c, symbols));

    /// <summary>
    ///     The <c>[assembly: Behavior(typeof(X))]</c> types of an assembly, in declaration order: <see cref="AssemblyBehaviors" /> for this
    ///     compilation's own assembly, read from metadata for a referenced one.
    /// </summary>
    public ImmutableArray<ITypeSymbol> AssemblyBehaviorsOf(IAssemblySymbol? assembly) =>
        assembly is null || SymbolEqualityComparer.Default.Equals(assembly, Compilation.Assembly)
            ? AssemblyBehaviors
            : _assemblyBehaviors.GetOrAdd(assembly, a => [.. ReadAssemblyBehaviors(a, Symbols)]);

    /// <summary>
    ///     The host's model of a concrete notification type (ADR 0001, Notifications): its publish strategy and its per-publish behaviors, or
    ///     <see langword="null" /> when the type is not a concrete notification (KRX011, KRX012 shapes excluded) or generated code cannot reach
    ///     it (narrower than <c>internal</c>). Built once per type.
    /// </summary>
    public HostNotificationModel? Notification(INamedTypeSymbol type) => _notifications.GetOrAdd(type, CreateNotification);

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
        var messageType = MessageType.Classify(type, Symbols);
        if (!messageType.IsNotificationOnly
         || !messageType.IsConcrete
         || messageType.IsUnsupportedShape
         || type.TypeKind == TypeKind.Error
         || TypeAccessibility.Of(type) == AccessLevel.Restricted)
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

    /// <summary>
    ///     The concrete notification type a handler candidate takes, when the host keeps the handler (it breaks no rule that stops generation,
    ///     and its class and signature are at least <c>internal</c>; OQ-24, OQ-29) and the parameter is a concrete notification the host can
    ///     reach; otherwise <see langword="null" />.
    /// </summary>
    private INamedTypeSymbol? HandledNotification(IMethodSymbol method)
    {
        if (HandlerModelFactory.Create(method, Symbols) is not { Kind: HandlerKind.Notification } model
         || model.ClassAccess == AccessLevel.Restricted
         || model.SignatureAccess == AccessLevel.Restricted)
        {
            return null;
        }

        for (var i = 0; i < model.Parameters.Count; i++)
        {
            if (model.Parameters[i].Role == ParameterRole.Message)
            {
                return method.Parameters[i].Type is INamedTypeSymbol type && Notification(type) is not null ? type : null;
            }
        }

        return null;
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

    private static void CollectTypes(INamespaceSymbol @namespace, Func<INamedTypeSymbol, bool> predicate, ImmutableArray<INamedTypeSymbol>.Builder types)
    {
        foreach (var member in @namespace.GetMembers())
        {
            if (member is INamespaceSymbol nested)
            {
                CollectTypes(nested, predicate, types);
            }
            else if (member is INamedTypeSymbol type)
            {
                CollectTypes(type, predicate, types);
            }
        }
    }

    private static void CollectTypes(INamedTypeSymbol type, Func<INamedTypeSymbol, bool> predicate, ImmutableArray<INamedTypeSymbol>.Builder types)
    {
        if (predicate(type))
        {
            types.Add(type);
        }

        foreach (var nested in type.GetTypeMembers())
        {
            CollectTypes(nested, predicate, types);
        }
    }

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
    ///     The closed behaviors that apply to a handler: the method's <c>[Behavior]</c>s, then the assembly's, then the
    ///     global ones, each closed over the target and kept when it fits; de-duplicated by closed type; minus the host's exclusions. A
    ///     behavior that does not fit is skipped (the analyzer reports KRX009 for one attached to the method), as is one the behavior rules
    ///     reject (KRX019, KRX020) or one generated code cannot reach (narrower than <c>internal</c>).
    /// </summary>
    public static (ImmutableArray<PipelineBehaviorModel> Behaviors, ImmutableArray<string> UsedExclusions) For(
        IMethodSymbol handler,
        BehaviorTarget target,
        BehaviorCatalog catalog
    )
    {
        var attached = new List<ITypeSymbol>();
        foreach (var attribute in handler.GetAttributes())
        {
            if (KeryxSymbols.IsAttribute(attribute, catalog.Symbols.BehaviorAttribute)
             && attribute.ConstructorArguments is [{ Kind: TypedConstantKind.Type, Value: ITypeSymbol type }])
            {
                attached.Add(type);
            }
        }

        return Compose(attached, catalog.AssemblyBehaviorsOf(handler.ContainingAssembly), target, catalog);
    }

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
