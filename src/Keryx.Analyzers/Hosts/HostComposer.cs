using System.Collections.Immutable;
using Keryx.Analyzers.Behaviors;
using Keryx.Analyzers.Diagnostics;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;
using Specular.Scanning;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     Composes a host (ADR 0001, How the host reads it): resolves the references' manifests (<see cref="KeryxReferenceModel" />) in the
///     compilation, merges the referenced handlers with the host's own (found by its syntax pipeline), applies <c>ExcludeHandler</c>, and
///     composes every pipeline from the three behavior scopes, global behaviors of the references included. It reads symbols, so it runs on
///     every compilation; its output is an equatable <see cref="HostModel" />, so the steps after it stay cached while nothing changed.
/// </summary>
internal static class HostComposer
{
    public static HostModel Compose(
        HostSettings settings,
        ImmutableArray<HostHandlerEntry> ownHandlers,
        ImmutableArray<string> ownNotifications,
        KeryxReferenceModel references,
        Compilation compilation,
        CancellationToken cancellationToken
    )
    {
        if (!settings.Enabled || KeryxSymbols.Create(compilation) is not { } symbols)
        {
            return HostModel.Empty(settings);
        }

        var context = new Context(settings, symbols);

        // ExcludeHandler: the host ignores a referenced handler class (ADR 0001, Host control). Matched against the manifest's name of the
        // class before anything else of the entry is resolved, so an excluded handler whose types the host cannot resolve (typically in a
        // test host without the handler's infrastructure) reports nothing.
        var handlerExclusions = HandlerExclusions(compilation, symbols);
        var usedHandlerExclusions = new HashSet<string>(StringComparer.Ordinal);
        var handled = new SortedSet<string>(ownHandlers.Select(static e => e.HandledType).OfType<string>(), StringComparer.Ordinal);

        // The references: their manifests (KRX032, KRX033), their global behaviors, request and notification types, and handlers (KRX034).
        var referencedGlobals = new List<INamedTypeSymbol>();
        var referencedNotifications = new List<INamedTypeSymbol>();
        var referencedRequests = new SortedDictionary<string, HostRequestModel>(StringComparer.Ordinal);
        var referencedHandlers = new List<ResolvedHandler>();
        var assemblyBehaviors = new Dictionary<string, ImmutableArray<ITypeSymbol>>(StringComparer.Ordinal);
        foreach (var assembly in references.Assemblies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (assembly.FormatVersion is not { } version)
            {
                context.Report(Descriptors.MissingManifest, null, assembly.Name);
                continue;
            }

            if (version > ReferenceScan.CurrentFormatVersion)
            {
                context.Report(
                    Descriptors.NewerManifestFormat,
                    null,
                    assembly.Name,
                    version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ReferenceScan.CurrentFormatVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
                );
                continue;
            }

            var attached = ImmutableArray.CreateBuilder<ITypeSymbol>(assembly.AssemblyBehaviors.Count);
            foreach (var behavior in assembly.AssemblyBehaviors)
            {
                if (context.ResolveBehavior(behavior, assembly.Name) is { } type)
                {
                    attached.Add(type);
                }
            }

            assemblyBehaviors[assembly.Name] = attached.ToImmutable();

            foreach (var global in assembly.GlobalBehaviors)
            {
                if (context.ResolveBehavior(global, assembly.Name) is { } type)
                {
                    referencedGlobals.Add(type);
                }
            }

            foreach (var message in assembly.Messages)
            {
                if (context.Resolve(message, assembly.Name) is not { } type)
                {
                    continue;
                }

                var classified = MessageType.Classify(type, symbols);
                if (classified.IsCoveredRequest)
                {
                    // Request types declared in a referenced host are covered too: the referencing host composes only the handlers that host
                    // keeps public (OQ-16).
                    var operand = CSharpText.TypeOf(type);
                    referencedRequests[operand] = new(operand, type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat), null);
                }
                else if (classified.IsNotificationOnly)
                {
                    referencedNotifications.Add(type);
                }
            }

            foreach (var handler in assembly.Handlers)
            {
                if (handlerExclusions.FirstOrDefault(e => e.Class.Equals(handler.HandlerClass)) is { Model: { } excluded })
                {
                    usedHandlerExclusions.Add(excluded.Operand);
                    continue;
                }

                if (context.Resolve(handler, assembly.Name, out var target) is { } resolved)
                {
                    referencedHandlers.Add(resolved);
                }
                else if (target is not null && handler.Kind is HandlerKind.Request or HandlerKind.Stream)
                {
                    // Left out for KRX034; its request still counts as handled, so KRX030 does not add a second error (OQ-47).
                    handled.Add(CSharpText.TypeOf(target));
                }
            }
        }

        foreach (var handler in referencedHandlers)
        {
            if (handler.Kind is HandlerKind.Request or HandlerKind.Stream)
            {
                handled.Add(CSharpText.TypeOf(handler.Target));
            }
        }

        var catalog = BehaviorCatalog.Create(compilation, symbols, referencedGlobals, assemblyBehaviors);

        // The notification types the host knows (OQ-42): declared in its source, taken by its own and its referenced handlers, declared in
        // its references.
        var ownMethods = ownHandlers.Select(entry => (Entry: entry, Method: HostModelFactory.FindHandler(entry, symbols))).ToArray();
        var known = new List<INamedTypeSymbol>();
        foreach (var name in ownNotifications.Distinct(StringComparer.Ordinal).OrderBy(static n => n, StringComparer.Ordinal))
        {
            if (compilation.Assembly.GetTypeByMetadataName(name) is { } type)
            {
                known.Add(type);
            }
        }

        foreach (var (_, method) in ownMethods)
        {
            if (method is not null && HandledNotification(method, symbols) is { } notification)
            {
                known.Add(notification);
            }
        }

        known.AddRange(referencedHandlers.Where(static h => h.Kind == HandlerKind.Notification).Select(static h => h.Target).OfType<INamedTypeSymbol>());
        known.AddRange(referencedNotifications);
        catalog.SetKnownNotifications(known);

        // The handlers, own then referenced, each with the assembly it comes from (KRX005 across assemblies).
        var handlers = new List<(HostHandlerModel Model, string Assembly, string Display, LocationInfo? Location)>();
        foreach (var (entry, method) in ownMethods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (method is not null && HostModelFactory.CreateHandler(method, catalog) is { } model)
            {
                handlers.Add((model, settings.AssemblyName, method.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat), entry.Location));
            }
        }

        foreach (var handler in referencedHandlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var display = handler.Class.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
            if (HostModelFactory.CreateReferencedHandler(handler.Class, handler.Target, handler.Kind, handler.Behaviors, handler.Order, handler.StaticInvoker, catalog, out var problem) is not { } model)
            {
                // The entry resolved but no longer describes a handler of the types the host resolves (version skew). Its request still
                // counts as handled, so KRX030 does not add a second error (OQ-47).
                context.Report(Descriptors.ManifestEntryDoesNotFit, null, display, handler.Assembly, problem ?? "");
                continue;
            }

            // KRX009 for the pairs that involve referenced behaviors: an attached behavior that does not fit the target as the host resolves
            // it would otherwise drop out of the pipeline silently.
            foreach (var (behavior, reason) in HostModelFactory.UnfitAttachedBehaviors(handler.Behaviors, handler.Target, handler.Kind, catalog))
            {
                context.Report(
                    Descriptors.BehaviorDoesNotFit,
                    null,
                    behavior.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                    display + ".Handle",
                    reason
                );
            }

            handlers.Add((model, handler.Assembly, display, null));
        }

        ReportDuplicates(context, handlers);

        foreach (var (exclusion, _) in handlerExclusions)
        {
            if (!usedHandlerExclusions.Contains(exclusion.Operand))
            {
                context.Report(Descriptors.UnusedExclusion, exclusion.Location, "ExcludeHandler", exclusion.Display, Descriptors.NotPartOfAnyPipeline(exclusion.Display));
            }
        }

        var notifications = new SortedDictionary<string, HostNotificationModel>(StringComparer.Ordinal);
        foreach (var type in catalog.KnownNotifications)
        {
            if (catalog.Notification(type) is { } model && !notifications.ContainsKey(model.Type))
            {
                notifications.Add(model.Type, model);
            }
        }

        // Behaviors the catalog skipped because generated code would name a constructor parameter type the host cannot resolve (OQ-47):
        // per-publish behaviors of referenced notification types, and assembly behaviors read from metadata.
        foreach (var (behavior, error) in catalog.UnresolvedConstructors)
        {
            context.Report(
                Descriptors.UnresolvedManifestType,
                null,
                error.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                behavior.ContainingAssembly?.Name ?? "",
                error.ContainingAssembly?.Identity.Name ?? behavior.ContainingAssembly?.Name ?? ""
            );
        }

        return new(
            settings,
            handlers.Select(static h => h.Model).ToImmutableArray(),
            notifications.Values.ToImmutableArray(),
            handled.ToImmutableArray(),
            referencedRequests.Values.ToImmutableArray(),
            GlobalBehaviors(catalog),
            BehaviorExclusions(catalog),
            ExternallyHandled(compilation, symbols),
            context.Diagnostics.ToImmutableArray()
        );
    }

    /// <summary>
    ///     The host's <c>[assembly: ExternallyHandled(typeof(X))]</c> entries (ADR 0001, Host control), for KRX030 and KRX036, in declaration
    ///     order.
    /// </summary>
    private static ImmutableArray<ExternallyHandledModel> ExternallyHandled(Compilation compilation, KeryxSymbols symbols)
    {
        var entries = ImmutableArray.CreateBuilder<ExternallyHandledModel>();
        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (KeryxSymbols.IsAttribute(attribute, symbols.ExternallyHandledAttribute)
             && attribute.ConstructorArguments is [{ Kind: TypedConstantKind.Type, Value: ITypeSymbol type }])
            {
                entries.Add(
                    new(
                        CSharpText.TypeOf(type),
                        type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                        // The same set KRX030 covers: an interface, abstract base or open generic is never reported, so naming one does nothing.
                        MessageType.Classify(type, symbols).IsCoveredRequest,
                        LocationInfo.From(HandlerAnalysis.AttributeLocation(attribute))
                    )
                );
            }
        }

        return entries.ToImmutable();
    }

    /// <summary>
    ///     KRX005 across assemblies: a request or stream request type with handlers in more than one assembly of the composed set, naming each
    ///     handler and its assembly. Duplicates within one assembly are the analyzer's (in the host) or broke that assembly's build.
    /// </summary>
    private static void ReportDuplicates(Context context, List<(HostHandlerModel Model, string Assembly, string Display, LocationInfo? Location)> handlers)
    {
        var byRequest = new SortedDictionary<string, List<(string Assembly, string Display, LocationInfo? Location)>>(StringComparer.Ordinal);
        foreach (var (model, assembly, display, location) in handlers)
        {
            if (model.Kind is not ( HandlerKind.Request or HandlerKind.Stream ))
            {
                continue;
            }

            if (!byRequest.TryGetValue(model.MessageType, out var list))
            {
                list = [];
                byRequest.Add(model.MessageType, list);
            }

            list.Add((assembly, display, location));
        }

        var assemblies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in byRequest)
        {
            assemblies.Clear();
            LocationInfo? location = null;
            foreach (var handler in pair.Value)
            {
                assemblies.Add(handler.Assembly);
                location ??= handler.Location;
            }

            if (assemblies.Count < 2)
            {
                continue;
            }

            pair.Value.Sort(static (left, right) =>
                {
                    var byAssembly = StringComparer.Ordinal.Compare(left.Assembly, right.Assembly);
                    return byAssembly != 0 ? byAssembly : StringComparer.Ordinal.Compare(left.Display, right.Display);
                }
            );
            var named = new List<string>(pair.Value.Count);
            foreach (var handler in pair.Value)
            {
                named.Add($"'{handler.Display}' (assembly '{handler.Assembly}')");
            }

            context.Report(Descriptors.DuplicateHandlerInHost, location, pair.Key.Replace("global::", ""), string.Join(", ", named));
        }
    }

    /// <summary>
    ///     The concrete notification type one of the host's own handlers takes, when the host keeps the handler (it breaks no rule that stops
    ///     generation, and its class and signature are at least <c>internal</c>; OQ-24, OQ-29); otherwise <see langword="null" />.
    /// </summary>
    private static INamedTypeSymbol? HandledNotification(IMethodSymbol method, KeryxSymbols symbols)
    {
        if (HandlerModelFactory.Create(method, symbols) is not { Kind: HandlerKind.Notification } model
         || model.ClassAccess == AccessLevel.Restricted
         || model.SignatureAccess == AccessLevel.Restricted)
        {
            return null;
        }

        for (var i = 0; i < model.Parameters.Count; i++)
        {
            if (model.Parameters[i].Role == ParameterRole.Message)
            {
                return method.Parameters[i].Type as INamedTypeSymbol;
            }
        }

        return null;
    }

    /// <summary>
    ///     The global behaviors the host applies, for KRX035: every <c>[GlobalBehavior]</c>, own or referenced, that the behavior rules accept
    ///     and no <c>ExcludeBehavior</c> removes entirely (an exclusion of one closing of an open global behavior leaves the others).
    /// </summary>
    private static ImmutableArray<GlobalBehaviorModel> GlobalBehaviors(BehaviorCatalog catalog)
    {
        var excluded = new HashSet<string>(catalog.Exclusions.Select(static e => BehaviorOrdering.Operand(e.Type)), StringComparer.Ordinal);
        var globals = ImmutableArray.CreateBuilder<GlobalBehaviorModel>();
        foreach (var (type, attribute) in catalog.GlobalBehaviors)
        {
            if (catalog.Shape(type) is null || excluded.Contains(BehaviorOrdering.DefinitionOperand(type)))
            {
                continue;
            }

            globals.Add(
                new(
                    type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                    type.ContainingAssembly?.Name ?? "",
                    LocationInfo.From(HandlerAnalysis.AttributeLocation(attribute) ?? type.Locations.FirstOrDefault())
                )
            );
        }

        return globals.ToImmutable();
    }

    /// <summary>The host's <c>ExcludeBehavior</c> entries, for KRX036.</summary>
    private static ImmutableArray<ExclusionModel> BehaviorExclusions(BehaviorCatalog catalog) =>
    [
        .. catalog.Exclusions.Select(static e => new ExclusionModel(
                                         BehaviorOrdering.Operand(e.Type),
                                         e.Type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                                         LocationInfo.From(HandlerAnalysis.AttributeLocation(e.Attribute))
                                     )
                    ),
    ];

    /// <summary>The host's <c>[assembly: ExcludeHandler(typeof(X))]</c> entries, each with the type as a manifest names it.</summary>
    private static ImmutableArray<(ExclusionModel Model, KeryxTypeReference Class)> HandlerExclusions(Compilation compilation, KeryxSymbols symbols)
    {
        var exclusions = ImmutableArray.CreateBuilder<(ExclusionModel, KeryxTypeReference)>();
        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (KeryxSymbols.IsAttribute(attribute, symbols.ExcludeHandlerAttribute)
             && attribute.ConstructorArguments is [{ Kind: TypedConstantKind.Type, Value: ITypeSymbol type }])
            {
                exclusions.Add(
                    (
                        new(
                            CSharpText.TypeOf(type),
                            type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                            LocationInfo.From(HandlerAnalysis.AttributeLocation(attribute))
                        ),
                        ReferenceScan.Reference(type)
                    )
                );
            }
        }

        return exclusions.ToImmutable();
    }

    /// <summary>A referenced handler whose manifest entry resolved in the host's compilation.</summary>
    private sealed record ResolvedHandler(
        string Assembly,
        INamedTypeSymbol Class,
        ITypeSymbol Target,
        HandlerKind Kind,
        ImmutableArray<ITypeSymbol> Behaviors,
        int Order,
        bool StaticInvoker
    );

    /// <summary>Resolves manifest names in the compilation and collects the diagnostics of one composition.</summary>
    private sealed class Context(HostSettings settings, KeryxSymbols symbols)
    {
        private readonly TypeReferenceResolver _resolver = new(symbols.Compilation, ReferenceScan.ReferencedAssemblies(symbols.Compilation));
        private readonly HashSet<string> _reported = [with(StringComparer.Ordinal)];

        public List<HostDiagnosticModel> Diagnostics { get; } = [];

        private Compilation Compilation => symbols.Compilation;

        /// <summary>Reports a diagnostic once, at <paramref name="location" /> or, without one, on <c>[assembly: KeryxHost]</c>.</summary>
        public void Report(DiagnosticDescriptor descriptor, LocationInfo? location, params string[] arguments)
        {
            if (_reported.Add(descriptor.Id + "|" + string.Join("|", arguments)))
            {
                Diagnostics.Add(new(descriptor, location ?? settings.HostAttribute, ImmutableArray.Create(arguments)));
            }
        }

        /// <summary>
        ///     The type a manifest names, or <see langword="null" /> after reporting KRX034 when it does not resolve, or is not accessible from
        ///     the host.
        /// </summary>
        public INamedTypeSymbol? Resolve(KeryxTypeReference reference, string manifestAssembly)
        {
            var type = _resolver.Resolve(reference.ToTypeReference());
            if (type is not null && !ContainsError(type) && Compilation.IsSymbolAccessibleWithin(type.OriginalDefinition, Compilation.Assembly))
            {
                return type;
            }

            Report(Descriptors.UnresolvedManifestType, null, reference.Display, manifestAssembly, MissingAssembly(reference));
            return null;
        }

        /// <summary>
        ///     A behavior a manifest names (a global behavior, an <c>[assembly: Behavior]</c> type or a handler's attached behavior), or
        ///     <see langword="null" /> after reporting KRX034 when it does not resolve, or when a parameter type of its public constructors, which
        ///     the generated factory names, does not (OQ-47).
        /// </summary>
        public INamedTypeSymbol? ResolveBehavior(KeryxTypeReference reference, string manifestAssembly) =>
            Resolve(reference, manifestAssembly) is { } type && ReportConstructorError(type, manifestAssembly) ? type : null;

        /// <summary>
        ///     Reports KRX034 for the first parameter type of a public instance constructor of <paramref name="type" /> that does not resolve;
        ///     <see langword="true" /> when there is none.
        /// </summary>
        private bool ReportConstructorError(INamedTypeSymbol type, string manifestAssembly)
        {
            if (ConstructorError(type) is not { } error)
            {
                return true;
            }

            Report(
                Descriptors.UnresolvedManifestType,
                null,
                error.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                manifestAssembly,
                error.ContainingAssembly?.Identity.Name ?? manifestAssembly
            );
            return false;
        }

        /// <summary>
        ///     A referenced handler's manifest entry, resolved: its class, target type and attached behaviors (each KRX034 when it does not
        ///     resolve), and the types the host derives from them that generated code names (its response type and its constructor's
        ///     parameters), which must resolve too. <see langword="null" /> when anything is missing.
        /// </summary>
        public ResolvedHandler? Resolve(ReferencedHandlerModel handler, string manifestAssembly, out ITypeSymbol? target)
        {
            var handlerClass = Resolve(handler.HandlerClass, manifestAssembly);
            target = handler.TargetType is { } targetType ? Resolve(targetType, manifestAssembly) : null;
            var behaviors = ImmutableArray.CreateBuilder<ITypeSymbol>(handler.Behaviors.Count);
            var resolved = handlerClass is not null && target is not null;
            foreach (var behavior in handler.Behaviors)
            {
                // An attached behavior the host cannot construct leaves the handler out, so its pipeline does not change silently.
                if (ResolveBehavior(behavior, manifestAssembly) is { } behaviorType)
                {
                    behaviors.Add(behaviorType);
                }
                else
                {
                    resolved = false;
                }
            }

            if (!resolved)
            {
                return null;
            }

            var classified = MessageType.Classify(target!, symbols);
            var derived = new List<ITypeSymbol>();
            if (( handler.Kind == HandlerKind.Stream ? classified.ItemType : classified.ResponseType ) is { } response)
            {
                derived.Add(response);
            }

            if (!handler.StaticInvoker)
            {
                derived.AddRange(handlerClass!.InstanceConstructors.Where(static c => c.DeclaredAccessibility == Accessibility.Public).SelectMany(static c => c.Parameters).Select(static p => p.Type));
            }

            foreach (var type in derived)
            {
                if (FirstError(type) is { } error)
                {
                    Report(
                        Descriptors.UnresolvedManifestType,
                        null,
                        error.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        manifestAssembly,
                        error.ContainingAssembly?.Identity.Name ?? manifestAssembly
                    );
                    resolved = false;
                }
            }

            return resolved
                ? new(manifestAssembly, handlerClass!, target!, handler.Kind, behaviors.MoveToImmutable(), handler.Order, handler.StaticInvoker)
                : null;
        }

        /// <summary>The assembly KRX034 names: the first one, in the reference or its type arguments, that the host does not reference.</summary>
        private string MissingAssembly(KeryxTypeReference reference)
        {
            if (_resolver.Resolve(( reference with { Arguments = EquatableArray<KeryxTypeReference>.Empty, Unbound = false } ).ToTypeReference()) is null)
            {
                return reference.Assembly;
            }

            foreach (var argument in reference.Arguments)
            {
                if (_resolver.Resolve(( argument with { ArrayRanks = EquatableArray<int>.Empty } ).ToTypeReference()) is null)
                {
                    return MissingAssembly(argument with { ArrayRanks = EquatableArray<int>.Empty });
                }
            }

            return reference.Assembly;
        }

        private static bool ContainsError(ITypeSymbol type) => FirstError(type) is not null;
    }

    /// <summary>
    ///     The first type, in <paramref name="type" /> or its type arguments or containing types, that does not resolve (an error type), or
    ///     <see langword="null" />.
    /// </summary>
    internal static ITypeSymbol? FirstError(ITypeSymbol type) =>
        type switch
        {
            { TypeKind: TypeKind.Error } => type,
            IArrayTypeSymbol array => FirstError(array.ElementType),
            INamedTypeSymbol { IsUnboundGenericType: false } named => named.TypeArguments.Select(FirstError).FirstOrDefault(static e => e is not null)
                                                                   ?? ( named.ContainingType is { } containing ? FirstError(containing) : null ),
            _ => null,
        };

    /// <summary>
    ///     The first parameter type of a public instance constructor of a behavior (its definition) that does not resolve, or
    ///     <see langword="null" />: generated factories name these types, so one the host cannot resolve breaks the generated code (OQ-47).
    /// </summary>
    internal static ITypeSymbol? ConstructorError(INamedTypeSymbol type) =>
        type.OriginalDefinition.InstanceConstructors
            .Where(static c => c.DeclaredAccessibility == Accessibility.Public)
            .SelectMany(static c => c.Parameters)
            .Select(static p => FirstError(p.Type))
            .FirstOrDefault(static e => e is not null);
}
