using System.Collections.Immutable;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;
using Specular.Scanning;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     The host's one <c>Scan</c> call over its references (ADR 0001, How the host reads it): the three scan requests, and the transform that
///     projects each referenced assembly's manifest into a <see cref="ReferencedAssemblyModel" />, cached per assembly in Specular's
///     ScanCache.
/// </summary>
internal static class ReferenceScan
{
    /// <summary>
    ///     The consumer id of the host's scan, which names its cache file (<c>obj/ctp/Keryx.ctpjson</c>). No other <c>Scan</c> call may use it:
    ///     each one writes the whole file.
    /// </summary>
    public const string ConsumerId = "Keryx";

    /// <summary>The ScanCache key of the per-assembly projection.</summary>
    public const string ProjectionKey = "Keryx.Manifest";

    /// <summary>
    ///     The version of the projection's shape, its derivation and its JSON contract (<see cref="AssemblyProjection" />). Bump it whenever
    ///     one of them changes, so projections cached by an older Keryx are recomputed.
    /// </summary>
    public const int ProjectionDataVersion = 1;

    /// <summary>The newest <c>KeryxManifest.FormatVersion</c> this Keryx reads (ADR 0001, Versioning). A newer one is KRX032.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>The <c>KeryxHandler.Kind</c> values a host understands.</summary>
    private const int MaxHandlerKind = (int)HandlerKind.Notification;

    /// <summary>
    ///     The three scan requests, in this order (results are read by position): handler classes (<c>WithAttribute(KeryxHandler)</c>), global
    ///     behaviors (<c>WithAttribute(GlobalBehavior)</c>), and non-abstract request, stream request and notification types. Each is pruned to
    ///     the referenced assemblies that reference Keryx, without the host itself. Empty when the compilation is not a host.
    /// </summary>
    public static EquatableArray<ScanRequest> CreateRequests(Compilation compilation)
    {
        if (KeryxSymbols.Create(compilation) is not { IsHost: true } symbols
         || symbols.KeryxHandlerAttribute is not { } handlerAttribute
         || symbols.GlobalBehaviorAttribute is not { } globalBehaviorAttribute)
        {
            return EquatableArray<ScanRequest>.Empty;
        }

        var marker = symbols.RequestOfT;
        return ImmutableArray.Create(
            ScanRequest.Build(
                compilation,
                s => s.FromAssemblies().DependenciesFromAssemblyOf(marker).NotFromAssembly(compilation.Assembly).GetTypes(f => f.WithAttribute(handlerAttribute))
            ),
            ScanRequest.Build(
                compilation,
                s => s.FromAssemblies().DependenciesFromAssemblyOf(marker).NotFromAssembly(compilation.Assembly).GetTypes(f => f.WithAttribute(globalBehaviorAttribute))
            ),
            ScanRequest.Build(
                compilation,
                s => s
                    .FromAssemblies()
                    .DependenciesFromAssemblyOf(marker)
                    .NotFromAssembly(compilation.Assembly)
                    .GetTypes(f => f.AssignableToAny(symbols.RequestOfT.ConstructUnboundGenericType(), symbols.StreamRequest.ConstructUnboundGenericType(), symbols.Notification)
                                    .NotInfoOf(TypeInfoFilter.Abstract))
            )
        );
    }

    /// <summary>
    ///     The scan's transform: one projection per referenced assembly that references Keryx, read from the ScanCache when the assembly is
    ///     unchanged (by MVID, or in memory for as long as the compiler keeps the same reference), otherwise read from its attributes and stored.
    /// </summary>
    public static KeryxReferenceModel Transform(ScanContext scan)
    {
        if (scan.Results.Count != 3 || KeryxSymbols.Create(scan.Compilation) is not { } symbols)
        {
            return KeryxReferenceModel.Empty;
        }

        var handlers = scan.Results[0];
        var globals = scan.Results[1];
        var messages = scan.Results[2];
        var assemblies = ImmutableArray.CreateBuilder<ReferencedAssemblyModel>(handlers.Assemblies.Count);
        var byName = ReferencedAssemblies(scan.Compilation);
        foreach (var scanned in handlers.Assemblies)
        {
            scan.CancellationToken.ThrowIfCancellationRequested();
            if (!byName.TryGetValue(scanned.Name, out var assembly))
            {
                continue;
            }

            if (!scan.Cache.TryGet(ProjectionKey, assembly, KeryxProjectionJsonContext.Default.AssemblyProjection, ProjectionDataVersion, out var projection))
            {
                projection = AssemblyProjection.From(Project(assembly, symbols, OfAssembly(handlers, scanned.Name), OfAssembly(globals, scanned.Name), OfAssembly(messages, scanned.Name)));
                scan.Cache.Set(ProjectionKey, assembly, projection, KeryxProjectionJsonContext.Default.AssemblyProjection, ProjectionDataVersion);
            }

            assemblies.Add(projection.ToModel());
        }

        return new(assemblies.ToImmutable());
    }

    /// <summary>The compilation's referenced assemblies (and its own) by metadata name, as the scan sees them.</summary>
    public static Dictionary<string, IAssemblySymbol> ReferencedAssemblies(Compilation compilation) => ScanAssemblies.ByName(ScanAssemblies.Get(compilation));

    /// <summary>
    ///     Reads one assembly's manifest: <c>[assembly: KeryxManifest]</c>, <c>[assembly: KeryxHost]</c>, its <c>[assembly: Behavior]</c>s,
    ///     and the arguments of the
    ///     <c>[KeryxHandler]</c> on each scanned handler class, plus the scanned global behaviors and message types. Only types the host can
    ///     reference are kept.
    /// </summary>
    public static ReferencedAssemblyModel Project(
        IAssemblySymbol assembly,
        KeryxSymbols symbols,
        IEnumerable<ScannedType> handlerTypes,
        IEnumerable<ScannedType> globalTypes,
        IEnumerable<ScannedType> messageTypes
    )
    {
        int? formatVersion = null;
        var isHost = false;
        var assemblyBehaviors = ImmutableArray.CreateBuilder<KeryxTypeReference>();
        foreach (var attribute in assembly.GetAttributes())
        {
            if (KeryxSymbols.IsAttribute(attribute, symbols.BehaviorAttribute)
             && attribute.ConstructorArguments is [{ Kind: TypedConstantKind.Type, Value: ITypeSymbol behavior }])
            {
                assemblyBehaviors.Add(Reference(behavior));
            }
            else if (KeryxSymbols.IsAttribute(attribute, symbols.KeryxManifestAttribute))
            {
                formatVersion = attribute.NamedArguments.FirstOrDefault(static a => a.Key == "FormatVersion").Value.Value is int version ? version : 0;
            }
            else if (KeryxSymbols.IsAttribute(attribute, symbols.KeryxHostAttribute))
            {
                isHost = true;
            }
        }

        var handlers = ImmutableArray.CreateBuilder<ReferencedHandlerModel>();
        foreach (var scanned in handlerTypes)
        {
            if (scanned.IsAccessible
             && ScanSymbolNames.FindType(assembly, scanned.MetadataName) is { } type
             && type.GetAttributes().FirstOrDefault(a => KeryxSymbols.IsAttribute(a, symbols.KeryxHandlerAttribute)) is { } attribute
             && Handler(type, attribute) is { } handler)
            {
                handlers.Add(handler);
            }
        }

        var globals = ImmutableArray.CreateBuilder<KeryxTypeReference>();
        foreach (var scanned in globalTypes)
        {
            if (scanned.IsAccessible && ScanSymbolNames.FindType(assembly, scanned.MetadataName) is { } type)
            {
                globals.Add(Reference(type.IsGenericType ? type.ConstructUnboundGenericType() : type));
            }
        }

        var messages = ImmutableArray.CreateBuilder<KeryxTypeReference>();
        foreach (var scanned in messageTypes)
        {
            if (scanned is { IsAccessible: true, Arity: 0 } && ScanSymbolNames.FindType(assembly, scanned.MetadataName) is { } type)
            {
                messages.Add(Reference(type));
            }
        }

        return new(
            assembly.MetadataName,
            formatVersion,
            isHost,
            handlers.ToImmutable(),
            globals.ToImmutable(),
            messages.ToImmutable(),
            assemblyBehaviors.ToImmutable()
        );
    }

    /// <summary>A type as a manifest names it. Type parameters and pointers, which a manifest never names, are kept by name only.</summary>
    public static KeryxTypeReference Reference(ITypeSymbol type)
    {
        var ranks = ImmutableArray.CreateBuilder<int>();
        while (type is IArrayTypeSymbol array)
        {
            ranks.Add(array.Rank);
            type = array.ElementType;
        }

        if (type is not INamedTypeSymbol named)
        {
            return new(type.ContainingAssembly?.Name ?? "", type.Name, false, ImmutableArray<KeryxTypeReference>.Empty, ranks.ToImmutable());
        }

        var arguments = ImmutableArray.CreateBuilder<KeryxTypeReference>();
        if (!named.IsUnboundGenericType)
        {
            var levels = new Stack<INamedTypeSymbol>();
            for (var level = named; level is not null; level = level.ContainingType)
            {
                levels.Push(level);
            }

            foreach (var level in levels)
            {
                if (!SymbolEqualityComparer.Default.Equals(level, level.OriginalDefinition))
                {
                    arguments.AddRange(level.TypeArguments.Select(Reference));
                }
            }
        }

        return new(
            named.ContainingAssembly?.Identity.Name ?? "",
            ScanSymbolNames.GetFullMetadataName(named.OriginalDefinition),
            named.IsUnboundGenericType,
            arguments.ToImmutable(),
            ranks.ToImmutable()
        );
    }

    private static ReferencedHandlerModel? Handler(INamedTypeSymbol type, AttributeData attribute)
    {
        if (attribute.ConstructorArguments is not [var target, { Value: int kind }] || kind is < 0 or > MaxHandlerKind)
        {
            return null;
        }

        var behaviors = ImmutableArray.CreateBuilder<KeryxTypeReference>();
        var order = 0;
        var staticInvoker = false;
        foreach (var argument in attribute.NamedArguments)
        {
            switch (argument.Key)
            {
                case "Behaviors" when argument.Value.Kind == TypedConstantKind.Array:
                    foreach (var behavior in argument.Value.Values)
                    {
                        if (behavior.Value is ITypeSymbol behaviorType)
                        {
                            behaviors.Add(Reference(behaviorType));
                        }
                    }

                    break;
                case "Order" when argument.Value.Value is int value:
                    order = value;
                    break;
                case "StaticInvoker" when argument.Value.Value is bool value:
                    staticInvoker = value;
                    break;
                default:
                    break;
            }
        }

        return new(Reference(type), target.Value is ITypeSymbol targetType ? Reference(targetType) : null, (HandlerKind)kind, behaviors.ToImmutable(), order, staticInvoker);
    }

    private static IEnumerable<ScannedType> OfAssembly(ScanResult result, string assemblyName) =>
        result.Types.Where(t => string.Equals(t.AssemblyName, assemblyName, StringComparison.Ordinal));
}
