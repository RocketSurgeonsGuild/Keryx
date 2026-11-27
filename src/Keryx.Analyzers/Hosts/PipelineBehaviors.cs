using System.Collections.Concurrent;
using System.Collections.Immutable;
using Keryx.Analyzers.Behaviors;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     A <c>[GlobalBehavior]</c> the host sees: the behavior class, the kinds the attribute makes global (every kind when it is on the class,
///     the method's kind when it is on a behavior method), and the attribute KRX035 points at.
/// </summary>
/// <param name="Type">The behavior class.</param>
/// <param name="Kinds">The kinds that are global.</param>
/// <param name="Attribute">The first <c>[GlobalBehavior]</c> (the class's, else a method's).</param>
internal sealed record GlobalBehavior(INamedTypeSymbol Type, ImmutableArray<BehaviorKind> Kinds, AttributeData Attribute)
{
    /// <summary>The global behavior of a class, or <see langword="null" /> when no <c>[GlobalBehavior]</c> applies to any of its kinds.</summary>
    public static GlobalBehavior? Of(INamedTypeSymbol type, KeryxSymbols symbols)
    {
        if (symbols.GlobalBehaviorAttribute is not { } globalAttribute)
        {
            return null;
        }

        var carried = BehaviorShapes.Kinds(type, symbols);
        if (carried.IsEmpty)
        {
            return null;
        }

        if (type.GetAttributes().FirstOrDefault(a => KeryxSymbols.IsAttribute(a, globalAttribute)) is { } classAttribute)
        {
            return new(type, [.. carried.Select(static k => k.Kind)], classAttribute);
        }

        AttributeData? first = null;
        var kinds = ImmutableArray.CreateBuilder<BehaviorKind>();
        foreach (var method in BehaviorShapes.Candidates(type))
        {
            if (method.GetAttributes().FirstOrDefault(a => KeryxSymbols.IsAttribute(a, globalAttribute)) is { } methodAttribute
             && BehaviorShapes.BehaviorMethodKind(method, symbols) is { } kind
             && !kinds.Contains(kind))
            {
                first ??= methodAttribute;
                kinds.Add(kind);
            }
        }

        return first is null ? null : new(type, kinds.ToImmutable(), first);
    }
}

/// <summary>
///     The behaviors and notification types a host composes its pipelines from, beyond those attached to a handler: <c>[assembly: Behavior]</c>
///     entries, <c>[GlobalBehavior]</c>s (from the host's source and from its references), the host's <c>[assembly: ExcludeBehavior]</c>
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
    private readonly ConcurrentDictionary<(INamedTypeSymbol Type, BehaviorKind Kind), ITypeSymbol?> _serviceErrors = new(ServiceKeyComparer.Instance);
    private readonly ConcurrentQueue<(INamedTypeSymbol Notification, ITypeSymbol Behavior, string Reason)> _unfitNotificationBehaviors = new();
    private readonly ConcurrentQueue<(ITypeSymbol Error, string Assembly)> _unresolvedNotificationBehaviors = new();

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

        var globals = ImmutableArray.CreateBuilder<GlobalBehavior>();
        CollectGlobals(compilation.Assembly.GlobalNamespace, symbols, globals);
        foreach (var reference in referencedGlobals)
        {
            if (GlobalBehavior.Of(reference, symbols) is { } global)
            {
                globals.Add(global);
            }
        }

        GlobalBehaviors = globals.ToImmutable();
    }

    public Compilation Compilation { get; }
    public KeryxSymbols Symbols { get; }

    /// <summary>The <c>[assembly: Behavior(typeof(X))]</c> types, in declaration order.</summary>
    public ImmutableArray<ITypeSymbol> AssemblyBehaviors { get; }

    /// <summary>
    ///     The global behaviors: those declared in this compilation (in namespace and declaration order), then the referenced ones (in assembly
    ///     and metadata name order).
    /// </summary>
    public ImmutableArray<GlobalBehavior> GlobalBehaviors { get; }

    /// <summary>The <c>[assembly: ExcludeBehavior(typeof(X))]</c> entries.</summary>
    public ImmutableArray<(ITypeSymbol Type, AttributeData Attribute)> Exclusions { get; }

    /// <summary>
    ///     Every concrete notification type the host knows (ADR 0001, Polymorphic handlers; OQ-42): the ones declared in its source, the
    ///     concrete notification parameter types of its handlers and of its referenced handlers, and the notification types of its references
    ///     (from the scan). The same set for every handler, so a type known through one handler reaches the polymorphic handlers too.
    /// </summary>
    public ImmutableArray<INamedTypeSymbol> KnownNotifications { get; private set; } = ImmutableArray<INamedTypeSymbol>.Empty;

    /// <summary>
    ///     The <c>[Behavior]</c>s on referenced notification types that do not fit the type as the host resolves it (OQ-55), for KRX009, in
    ///     the order they were found.
    /// </summary>
    public IEnumerable<(INamedTypeSymbol Notification, ITypeSymbol Behavior, string Reason)> UnfitNotificationBehaviors => _unfitNotificationBehaviors;

    /// <summary>
    ///     The <c>[Behavior]</c>s on referenced notification types whose behavior class does not resolve in the host (its assembly is not
    ///     referenced), for KRX034: the first type that does not resolve and the assembly that declares the notification type, in the order
    ///     they were found. Each one is skipped, and the rest of the publish pipeline kept.
    /// </summary>
    public IEnumerable<(ITypeSymbol Error, string Assembly)> UnresolvedNotificationBehaviors => _unresolvedNotificationBehaviors;

    /// <summary>A catalog for one composition of a host.</summary>
    /// <param name="compilation">The host's compilation.</param>
    /// <param name="symbols">Its Keryx symbols.</param>
    /// <param name="referencedGlobals">The behavior classes of its references that carry <c>[GlobalBehavior]</c> on the class or a method.</param>
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
    ///     The host's model of a concrete notification type (ADR 0001, Notifications): its publish strategy and its notification behaviors, or
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

        // The analyzer reports KRX009 for a [Behavior] on a notification type in the host's own source; the host reports it for a referenced
        // type, whose behavior may no longer fit the type as the host resolves it (OQ-55). A behavior class of a referenced type that does
        // not resolve (its assembly is not referenced) is KRX034, like any type the host derives from a reference, and is skipped.
        var inSource = SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, Compilation.Assembly);
        var parallel = false;
        var attached = ImmutableArray.CreateBuilder<ITypeSymbol>();
        foreach (var attribute in type.GetAttributes())
        {
            if (KeryxSymbols.IsAttribute(attribute, Symbols.PublishStrategyAttribute) && attribute.ConstructorArguments is [{ Value: int strategy }])
            {
                parallel = strategy == ParallelStrategy;
            }
            else if (KeryxSymbols.IsAttribute(attribute, Symbols.BehaviorAttribute)
                  && attribute.ConstructorArguments is [{ Kind: TypedConstantKind.Type or TypedConstantKind.Error, Value: ITypeSymbol behavior }])
            {
                if (HostComposer.FirstError(behavior) is { } error)
                {
                    if (!inSource)
                    {
                        _unresolvedNotificationBehaviors.Enqueue((error, type.ContainingAssembly?.Name ?? ""));
                    }

                    continue;
                }

                attached.Add(behavior);
            }
        }

        var (behaviors, services, usedExclusions, serviceProvider) = PipelineBehaviors.Compose(
            attached,
            AssemblyBehaviorsOf(type.ContainingAssembly),
            new(BehaviorKind.Notification, type, null),
            this,
            inSource ? null : (behavior, reason) => _unfitNotificationBehaviors.Enqueue((type, behavior, reason))
        );

        return new(
            CSharpText.TypeOf(type),
            type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
            type.IsValueType,
            type.IsValueType || type.IsSealed,
            parallel,
            behaviors,
            usedExclusions,
            LocationInfo.From(type.Locations.FirstOrDefault()),
            services,
            serviceProvider
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
    ///     Whether generated code can call a behavior class's behavior method of <paramref name="kind" />: <see langword="false" /> for a
    ///     behavior from another assembly whose behavior method of that kind injects a type the host cannot resolve (OQ-47; per kind, OQ-57).
    ///     Those are listed in <see cref="UnresolvedServices" />.
    /// </summary>
    public bool CanCall(INamedTypeSymbol type, BehaviorKind kind) =>
        SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, Compilation.Assembly)
     || _serviceErrors.GetOrAdd((type, kind), key => HostComposer.ServiceError(key.Type, key.Kind, Symbols)) is null;

    /// <summary>
    ///     The behavior methods <see cref="CanCall" /> rejected so far, each with its class, kind and the first type that does not resolve, in
    ///     name and kind order, for KRX034.
    /// </summary>
    public IEnumerable<(INamedTypeSymbol Behavior, BehaviorKind Kind, ITypeSymbol Error)> UnresolvedServices =>
        _serviceErrors
           .Where(static pair => pair.Value is not null)
           .Select(static pair => (pair.Key.Type, pair.Key.Kind, pair.Value!))
           .OrderBy(static entry => entry.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
           .ThenBy(static entry => entry.Kind);

    /// <summary>Compares (behavior class, kind) keys by symbol.</summary>
    private sealed class ServiceKeyComparer : IEqualityComparer<(INamedTypeSymbol Type, BehaviorKind Kind)>
    {
        public static readonly ServiceKeyComparer Instance = new();

        public bool Equals((INamedTypeSymbol Type, BehaviorKind Kind) x, (INamedTypeSymbol Type, BehaviorKind Kind) y) =>
            x.Kind == y.Kind && SymbolEqualityComparer.Default.Equals(x.Type, y.Type);

        public int GetHashCode((INamedTypeSymbol Type, BehaviorKind Kind) obj) => SymbolEqualityComparer.Default.GetHashCode(obj.Type) * 3 + (int)obj.Kind;
    }

    /// <summary>The shape of a behavior class, or <see langword="null" /> when the behavior rules reject it or it is not one.</summary>
    public BehaviorShape? Shape(INamedTypeSymbol type) =>
        _shapes.GetOrAdd(type, definition => BehaviorShapes.Analyze(definition, Symbols, new IssueCollector()));

    private static void CollectGlobals(INamespaceSymbol @namespace, KeryxSymbols symbols, ImmutableArray<GlobalBehavior>.Builder globals)
    {
        foreach (var member in @namespace.GetMembers())
        {
            if (member is INamespaceSymbol nested)
            {
                CollectGlobals(nested, symbols, globals);
            }
            else if (member is INamedTypeSymbol type)
            {
                CollectGlobals(type, symbols, globals);
            }
        }
    }

    private static void CollectGlobals(INamedTypeSymbol type, KeryxSymbols symbols, ImmutableArray<GlobalBehavior>.Builder globals)
    {
        if (GlobalBehavior.Of(type, symbols) is { } global)
        {
            globals.Add(global);
        }

        foreach (var nested in type.GetTypeMembers())
        {
            CollectGlobals(nested, symbols, globals);
        }
    }
}

/// <summary>The behaviors of one pipeline, the services its context shares among them, and the exclusions that removed behaviors from it.</summary>
/// <param name="Behaviors">The behaviors, in no particular order (ordinal by class), each with its <see cref="PipelineBehaviorModel.ServiceSlots" />.</param>
/// <param name="Services">
///     The distinct services the behaviors inject, without the unkeyed <c>IServiceProvider</c>, in the stable order of
///     <see cref="PipelineBehaviors.CompareServices" />.
/// </param>
/// <param name="UsedExclusions">The <c>ExcludeBehavior</c> operands that removed a behavior.</param>
/// <param name="ServiceProvider">Whether a behavior injects the unkeyed <c>IServiceProvider</c>, which the context holds in its own field.</param>
internal sealed record ComposedBehaviors(
    ImmutableArray<PipelineBehaviorModel> Behaviors,
    ImmutableArray<ContextServiceModel> Services,
    ImmutableArray<string> UsedExclusions,
    bool ServiceProvider
)
{
    /// <summary>A pipeline without behaviors.</summary>
    public static ComposedBehaviors Empty { get; } = new([], [], [], false);
}

/// <summary>
///     Composes the behaviors of one pipeline from the three scopes (ADR 0001, Three ways to attach a behavior) and projects them into
///     <see cref="PipelineBehaviorModel" />s.
/// </summary>
internal static class PipelineBehaviors
{
    /// <summary>
    ///     The behaviors that apply to a request or stream handler: the behavior classes attached to the handler method (read from its
    ///     <c>[Behavior]</c>s for one of the host's own handlers, from <c>KeryxHandler.Behaviors</c> for a referenced one), then its assembly's,
    ///     then the global ones, each kept when its behavior method of the target's kind fits; de-duplicated by behavior class; minus the
    ///     host's exclusions. A behavior that does not fit is skipped (KRX009 for one attached to the method: the analyzer reports it for the
    ///     host's own handlers, the host for referenced ones), as is one the behavior rules reject (KRX019), one generated code cannot reach
    ///     (narrower than <c>internal</c>, or not accessible from the host) or one whose behavior method of the target's kind injects a type the
    ///     host cannot resolve (KRX034).
    /// </summary>
    public static ComposedBehaviors For(
        ImmutableArray<ITypeSymbol> attached,
        IAssemblySymbol? handlerAssembly,
        BehaviorTarget target,
        BehaviorCatalog catalog
    ) =>
        Compose(attached, catalog.AssemblyBehaviorsOf(handlerAssembly), target, catalog, null);

    /// <summary>
    ///     The behaviors that apply to one target: <paramref name="attached" /> (per target), then <paramref name="assembly" /> (per
    ///     assembly), then the global ones whose <c>[GlobalBehavior]</c> covers the target's kind, each kept when it fits; de-duplicated by
    ///     behavior class; minus the host's exclusions. <paramref name="unfitAttached" /> receives each attached behavior that does not fit,
    ///     with the reason (a class the behavior rules reject is its own KRX019 and is not passed). The pipeline's injected services are
    ///     shared (<see cref="ShareServices" />).
    /// </summary>
    public static ComposedBehaviors Compose(
        IEnumerable<ITypeSymbol> attached,
        IEnumerable<ITypeSymbol> assembly,
        BehaviorTarget target,
        BehaviorCatalog catalog,
        Action<ITypeSymbol, string>? unfitAttached
    )
    {
        var behaviors = new SortedDictionary<string, PipelineBehaviorModel>(StringComparer.Ordinal);
        var usedExclusions = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var type in attached)
        {
            Apply(type, unfitAttached);
        }

        foreach (var type in assembly)
        {
            Apply(type, null);
        }

        foreach (var global in catalog.GlobalBehaviors)
        {
            if (global.Kinds.Contains(target.Kind))
            {
                Apply(global.Type, null);
            }
        }

        var (shared, services, serviceProvider) = ShareServices([.. behaviors.Values]);
        return new(shared, services, [.. usedExclusions], serviceProvider);

        void Apply(ITypeSymbol type, Action<ITypeSymbol, string>? unfit)
        {
            if (type is not INamedTypeSymbol named || !BehaviorShapes.IsBehaviorClass(named, catalog.Symbols))
            {
                unfit?.Invoke(type, "it is not a behavior class (a static class with [RequestBehavior], [StreamBehavior] or [NotificationBehavior])");
                return;
            }

            if (catalog.Shape(named) is not { } shape || !catalog.CanCall(named, target.Kind))
            {
                return;
            }

            if (!BehaviorMatching.TryMatch(shape, target, catalog.Compilation, out var match, out var reason))
            {
                unfit?.Invoke(type, reason);
                return;
            }

            if (BehaviorShapes.Access(named, match.Method.Method) == AccessLevel.Restricted
             || !catalog.Compilation.IsSymbolAccessibleWithin(match.Method.Method, catalog.Compilation.Assembly))
            {
                return;
            }

            var identity = BehaviorOrdering.Operand(named);
            foreach (var (excluded, _) in catalog.Exclusions)
            {
                var operand = BehaviorOrdering.Operand(excluded);
                if (string.Equals(operand, identity, StringComparison.Ordinal))
                {
                    usedExclusions.Add(operand);
                    return;
                }
            }

            if (!behaviors.ContainsKey(identity))
            {
                behaviors.Add(identity, Model(named, match, catalog));
            }
        }
    }

    /// <summary>
    ///     The slot (<see cref="PipelineBehaviorModel.ServiceSlots" />) of an injected parameter that receives the unkeyed
    ///     <c>IServiceProvider</c>: the pipeline context's own <c>ServiceProvider</c> field, outside the numbered services (OQ-63).
    /// </summary>
    public const int ServiceProviderSlot = -1;

    /// <summary>
    ///     Gives a pipeline's injected services one pipeline context field each (ADR 0001, Injected parameters): the distinct
    ///     (service type, key) pairs the behavior methods inject, numbered by a stable ordinal sort (<see cref="CompareServices" />) so that
    ///     any edit that keeps the set keeps every number (OQ-64), and for each behavior the index of the service each of its injected
    ///     parameters receives. A service with at least one required consumer is resolved as required for all of them; one whose consumers
    ///     are all optional is resolved once as optional, and each call site applies its own default. The unkeyed <c>IServiceProvider</c> is
    ///     not a numbered service: its parameters get <see cref="ServiceProviderSlot" /> and the context holds the provider it is built with
    ///     (OQ-63); a keyed <c>IServiceProvider</c> is an ordinary keyed service. The behaviors keep their order.
    /// </summary>
    /// <param name="behaviors">The pipeline's behaviors, in any order.</param>
    public static (ImmutableArray<PipelineBehaviorModel> Behaviors, ImmutableArray<ContextServiceModel> Services, bool ServiceProvider) ShareServices(
        ImmutableArray<PipelineBehaviorModel> behaviors
    )
    {
        var distinct = new List<ContextServiceModel>();
        var serviceProvider = false;
        foreach (var behavior in behaviors)
        {
            foreach (var parameter in behavior.Services)
            {
                if (IsScopeProvider(parameter))
                {
                    serviceProvider = true;
                    continue;
                }

                var service = ContextServiceModel.For(parameter);
                var index = distinct.FindIndex(s => s.Matches(parameter));
                if (index < 0)
                {
                    distinct.Add(service);
                }
                else if (service.Required && !distinct[index].Required)
                {
                    distinct[index] = distinct[index] with { Required = true };
                }
            }
        }

        distinct.Sort(CompareServices);
        return ([.. behaviors.Select(b => b with { ServiceSlots = b.Services.Select(Slot).ToImmutableArray() })], [.. distinct], serviceProvider);

        int Slot(ServiceParameterModel parameter) =>
            IsScopeProvider(parameter) ? ServiceProviderSlot : distinct.FindIndex(s => s.Matches(parameter));
    }

    /// <summary>
    ///     The order of a pipeline context's numbered services (OQ-64): the service type ordinally (fully qualified, without a top-level
    ///     nullable reference annotation), then the key: no key first, then the key's C# literal text ordinally.
    /// </summary>
    internal static int CompareServices(ContextServiceModel x, ContextServiceModel y)
    {
        var byType = string.CompareOrdinal(x.ServiceType, y.ServiceType);
        return byType != 0 ? byType
            : x.ServiceKey is null ? y.ServiceKey is null ? 0 : -1
            : y.ServiceKey is null ? 1
            : string.CompareOrdinal(x.ServiceKey, y.ServiceKey);
    }

    /// <summary>Whether a parameter receives the scope's own provider: the unkeyed <c>IServiceProvider</c>.</summary>
    private static bool IsScopeProvider(ServiceParameterModel parameter) => parameter is { IsServiceProvider: true, ServiceKey: null };

    /// <summary>
    ///     The target of a request or stream request handler, or <see langword="null" /> for a notification handler, which has no behaviors of
    ///     its own (ADR 0001, Notifications).
    /// </summary>
    public static BehaviorTarget? Target(HandlerKind kind, ITypeSymbol message, ITypeSymbol? response) =>
        (kind, response) switch
        {
            (HandlerKind.Request, { } responseType) => new BehaviorTarget(BehaviorKind.Request, message, responseType),
            (HandlerKind.Stream, { } item) => new BehaviorTarget(BehaviorKind.Stream, message, item),
            _ => null,
        };

    private static PipelineBehaviorModel Model(INamedTypeSymbol type, BehaviorMatch match, BehaviorCatalog catalog)
    {
        var symbols = catalog.Symbols;
        var method = match.Method.Method;
        var (phase, order) = BehaviorOrdering.ReadPosition(method, symbols);
        var edges = BehaviorOrdering
                   .ReadEdges(method, symbols)
                   .Select(static e => new BehaviorEdgeModel(
                               new(e.RunsAfter, BehaviorOrdering.Operand(e.Target)),
                               LocationInfo.From(HandlerAnalysis.AttributeLocation(e.Attribute))
                           )
                    )
                   .ToImmutableArray();

        // The injected parameters, with the target's types substituted (ILogger<TRequest> becomes ILogger<Ping>).
        var services = ImmutableArray.CreateBuilder<ServiceParameterModel>(match.Services.Length);
        var next = 0;
        for (var i = 0; i < match.Method.Roles.Length; i++)
        {
            if (match.Method.Roles[i] == BehaviorParameterRole.Service)
            {
                var parameter = method.Parameters[i];
                services.Add(HostModelFactory.Service(match.Services[next++], HandlerModelFactory.ServiceKey(parameter), CSharpText.DefaultValue(parameter)));
            }
        }

        return new(
            CSharpText.Type(type),
            BehaviorOrdering.Operand(type),
            match.Method.Kind,
            method.Name,
            match.TypeArguments.Select(static t => t is null ? "" : CSharpText.Type(t)).ToImmutableArray(),
            match.Method.Roles,
            phase,
            order,
            BehaviorOrdering.OverridesOrdering(method, symbols),
            edges,
            services.MoveToImmutable(),
            type.ContainingAssembly?.Name ?? "",
            SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, catalog.Compilation.Assembly),
            match.ObliviousFit
        );
    }
}
