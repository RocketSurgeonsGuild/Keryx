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

    private readonly ConcurrentDictionary<INamedTypeSymbol, BehaviorShape?> _shapes = new(SymbolEqualityComparer.Default);

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

    public static BehaviorCatalog For(Compilation compilation, KeryxSymbols symbols) =>
        Catalogs.GetValue(compilation, c => new(c, symbols));

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
    ///     The closed behaviors that apply to a request or stream request handler: the method's <c>[Behavior]</c>s, then the assembly's, then the
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
        var symbols = catalog.Symbols;
        var behaviors = new SortedDictionary<string, PipelineBehaviorModel>(StringComparer.Ordinal);
        var usedExclusions = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var attribute in handler.GetAttributes())
        {
            if (KeryxSymbols.IsAttribute(attribute, symbols.BehaviorAttribute) && attribute.ConstructorArguments is [{ Kind: TypedConstantKind.Type, Value: ITypeSymbol type }])
            {
                Apply(type);
            }
        }

        foreach (var type in catalog.AssemblyBehaviors)
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

    /// <summary>The target of a request or stream request handler, or <see langword="null" /> for a notification handler (Phase 6).</summary>
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
