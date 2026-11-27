using System.Collections.Immutable;
using Keryx.Analyzers.Behaviors;
using Keryx.Analyzers.Diagnostics;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;
using Specular.Scanning;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     Composes a host (ADR 0001, How the host reads it): resolves the references' manifests (<see cref="KeryxReferenceModel" />) in the
///     compilation, merges the referenced handlers with the host's own (found by its syntax pipeline), applies <c>ExcludeHandler</c> to
///     referenced notification handlers (KRX039 for a request or stream request handler, which stays), and
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

        var context = new Context(symbols);

        // ExcludeHandler: the host ignores a referenced notification handler class (ADR 0001, Host control). Matched against the manifest's
        // name of the class before anything else of the entry is resolved, so an excluded handler whose types the host cannot resolve
        // (typically in a test host without the handler's infrastructure) reports nothing. A request or stream request handler, referenced
        // or the host's own, cannot be excluded (KRX039, OQ-59): its request's one handler lives in the request's assembly (KRX023), and a
        // test swaps its behavior through the services it injects.
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

            // Assembly and global behaviors are resolved before the host knows which of their kinds any pipeline uses, so each behavior
            // method of the kinds they apply to is checked here (KRX034 per kind, OQ-57); the class stays in the pipelines of the other kinds.
            var attached = ImmutableArray.CreateBuilder<ITypeSymbol>(assembly.AssemblyBehaviors.Count);
            foreach (var behavior in assembly.AssemblyBehaviors)
            {
                if (context.Resolve(behavior, assembly.Name) is { } type)
                {
                    context.ReportServiceErrors(type, null);
                    attached.Add(type);
                }
            }

            assemblyBehaviors[assembly.Name] = attached.ToImmutable();

            foreach (var global in assembly.GlobalBehaviors)
            {
                if (context.Resolve(global, assembly.Name) is { } type)
                {
                    if (GlobalBehavior.Of(type, symbols) is { } globalBehavior)
                    {
                        context.ReportServiceErrors(type, globalBehavior);
                    }

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
                    if (handler.Kind is not ( HandlerKind.Request or HandlerKind.Stream ))
                    {
                        continue;
                    }

                    // KRX039: the handler is composed as if the exclusion were not there.
                    context.Report(Descriptors.ExcludedRequestHandler, excluded.Location, excluded.Display, handler.Kind == HandlerKind.Request ? "request" : "stream request");
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

        // KRX039 for an ExcludeHandler that names one of the host's own request or stream request handlers (OQ-59). The host's own
        // notification handler is not a referenced handler, so excluding it has no effect (KRX036, OQ-45).
        foreach (var (entry, method) in ownMethods)
        {
            if (method is null || entry.HandledType is null)
            {
                continue;
            }

            foreach (var (exclusion, _, type) in handlerExclusions)
            {
                if (SymbolEqualityComparer.Default.Equals(type, method.ContainingType) && usedHandlerExclusions.Add(exclusion.Operand))
                {
                    var stream = method.Parameters.Any(p => MessageType.Classify(p.Type, symbols).IsStream);
                    context.Report(Descriptors.ExcludedRequestHandler, exclusion.Location, exclusion.Display, stream ? "stream request" : "request");
                }
            }
        }

        // The handlers, own then referenced. A request type with handlers in two assemblies cannot come from generated manifests (KRX023);
        // the host dispatches neither (OQ-60).
        var handlers = new List<HostHandlerModel>();
        foreach (var (_, method) in ownMethods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (method is not null && HostModelFactory.CreateHandler(method, catalog) is { } model)
            {
                handlers.Add(model);
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

            // KRX038: a referenced request or stream request type that is not sealed (its assembly suppressed KRX024) is not composed, so no
            // generated path depends on it (OQ-68); sending it throws KeryxHandlerNotFoundException. Its request counts as handled, so KRX030
            // does not add a second error.
            if (handler.Kind is HandlerKind.Request or HandlerKind.Stream && !HostModelFactory.IsSealed(handler.Target))
            {
                context.Report(
                    Descriptors.UnsealedRequestInHost,
                    null,
                    handler.Kind == HandlerKind.Request ? "Request type" : "Stream request type",
                    handler.Target.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                    handler.Target.ContainingAssembly?.Identity.Name ?? handler.Assembly
                );
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

            handlers.Add(model);
        }

        foreach (var (exclusion, _, _) in handlerExclusions)
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

        // KRX009 for a [Behavior] on a referenced notification type that does not fit the type as the host resolves it (OQ-55): the behavior
        // is skipped and the rest of the publish pipeline kept.
        foreach (var (notification, behavior, reason) in catalog.UnfitNotificationBehaviors)
        {
            context.Report(
                Descriptors.BehaviorDoesNotFit,
                null,
                behavior.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                notification.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                reason
            );
        }

        // KRX034 for a [Behavior] on a referenced notification type whose behavior class the host cannot resolve: the behavior is skipped and
        // the rest of the publish pipeline kept.
        foreach (var (error, assembly) in catalog.UnresolvedNotificationBehaviors)
        {
            context.Report(
                Descriptors.UnresolvedManifestType,
                null,
                error.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                assembly,
                error.ContainingAssembly?.Identity.Name ?? assembly
            );
        }

        // Behavior methods the catalog skipped because generated code would name an injected parameter type the host cannot resolve (OQ-47),
        // per kind (OQ-57): notification behaviors of referenced notification types, and assembly behaviors read from metadata. Those the
        // manifests name were reported above, with the same arguments, so they are not reported twice.
        foreach (var (behavior, kind, error) in catalog.UnresolvedServices)
        {
            context.ReportServiceError(behavior, kind, error);
        }

        return new(
            settings,
            handlers.ToImmutableArray(),
            notifications.Values.ToImmutableArray(),
            handled.ToImmutableArray(),
            referencedRequests.Values.ToImmutableArray(),
            GlobalBehaviors(catalog),
            BehaviorExclusions(catalog),
            context.Diagnostics.ToImmutableArray()
        );
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
    ///     The global behaviors the host applies, for KRX035: every <c>[GlobalBehavior]</c>, own or referenced, on a class or a behavior
    ///     method, that the behavior rules accept and no <c>ExcludeBehavior</c> removes, once per kind it makes global, except a kind whose
    ///     behavior method injects a type the host cannot resolve (KRX034, OQ-57).
    /// </summary>
    private static ImmutableArray<GlobalBehaviorModel> GlobalBehaviors(BehaviorCatalog catalog)
    {
        var excluded = new HashSet<string>(catalog.Exclusions.Select(static e => BehaviorOrdering.Operand(e.Type)), StringComparer.Ordinal);
        var globals = ImmutableArray.CreateBuilder<GlobalBehaviorModel>();
        foreach (var global in catalog.GlobalBehaviors)
        {
            if (catalog.Shape(global.Type) is null || excluded.Contains(BehaviorOrdering.Operand(global.Type)))
            {
                continue;
            }

            foreach (var (kind, _) in BehaviorShapes.Kinds(global.Type, catalog.Symbols))
            {
                if (!global.Kinds.Contains(kind) || !catalog.CanCall(global.Type, kind))
                {
                    continue;
                }

                globals.Add(
                    new(
                        global.Type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                        kind,
                        global.Type.ContainingAssembly?.Name ?? "",
                        LocationInfo.From(HandlerAnalysis.AttributeLocation(global.Attribute) ?? global.Type.Locations.FirstOrDefault())
                    )
                );
            }
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

    /// <summary>
    ///     The host's <c>[assembly: ExcludeHandler(typeof(X))]</c> entries, each with the type as a manifest names it and the type itself
    ///     (for the host's own handlers, KRX039).
    /// </summary>
    private static ImmutableArray<(ExclusionModel Model, KeryxTypeReference Class, ITypeSymbol Type)> HandlerExclusions(Compilation compilation, KeryxSymbols symbols)
    {
        var exclusions = ImmutableArray.CreateBuilder<(ExclusionModel, KeryxTypeReference, ITypeSymbol)>();
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
                        ReferenceScan.Reference(type),
                        type
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
    private sealed class Context(KeryxSymbols symbols)
    {
        private readonly TypeReferenceResolver _resolver = new(symbols.Compilation, ReferenceScan.ReferencedAssemblies(symbols.Compilation));
        private readonly HashSet<string> _reported = [with(StringComparer.Ordinal)];

        public List<HostDiagnosticModel> Diagnostics { get; } = [];

        private Compilation Compilation => symbols.Compilation;

        /// <summary>Reports a diagnostic once, at <paramref name="location" /> or, without one, with no location.</summary>
        public void Report(DiagnosticDescriptor descriptor, LocationInfo? location, params string[] arguments)
        {
            if (_reported.Add(descriptor.Id + "|" + string.Join("|", arguments)))
            {
                Diagnostics.Add(new(descriptor, location, ImmutableArray.Create(arguments)));
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
        ///     Reports KRX034 for each kind whose behavior method of <paramref name="type" /> injects a type that does not resolve, which the
        ///     generated pipeline context would name (OQ-47, per kind since OQ-57): every kind the class carries, or only the kinds
        ///     <paramref name="global" /> makes global.
        /// </summary>
        public void ReportServiceErrors(INamedTypeSymbol type, GlobalBehavior? global)
        {
            foreach (var (kind, _) in BehaviorShapes.Kinds(type, symbols))
            {
                if (( global is null || global.Kinds.Contains(kind) ) && ServiceError(type, kind, symbols) is { } error)
                {
                    ReportServiceError(type, kind, error);
                }
            }
        }

        /// <summary>
        ///     Reports KRX034 for the behavior method of <paramref name="kind" /> of <paramref name="type" />, whose injected parameter type
        ///     <paramref name="error" /> does not resolve.
        /// </summary>
        public void ReportServiceError(INamedTypeSymbol type, BehaviorKind kind, ITypeSymbol error)
        {
            var declaring = type.ContainingAssembly?.Name ?? "";
            Report(
                Descriptors.UnresolvedBehaviorService,
                null,
                error.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                BehaviorShapes.KindName(kind),
                BehaviorShapes.CandidateOfKind(type, kind, symbols) is { } method ? BehaviorShapes.Owner(method) : type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                declaring,
                error.ContainingAssembly?.Identity.Name ?? declaring
            );
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
            BehaviorKind? behaviorKind = handler.Kind switch
            {
                HandlerKind.Request => BehaviorKind.Request,
                HandlerKind.Stream => BehaviorKind.Stream,
                _ => null,
            };
            foreach (var behavior in handler.Behaviors)
            {
                // An attached behavior the host cannot resolve, or whose behavior method of the handler's kind injects a type the host cannot
                // resolve (OQ-47, OQ-57), leaves the handler out, so its pipeline does not change silently. Its methods of other kinds do not
                // matter here.
                if (Resolve(behavior, manifestAssembly) is not { } behaviorType)
                {
                    resolved = false;
                    continue;
                }

                // Checked only while the entry resolves: otherwise its message type may be what does not resolve, already reported.
                if (resolved && behaviorKind is { } kind && ServiceError(behaviorType, kind, symbols) is { } error)
                {
                    ReportServiceError(behaviorType, kind, error);
                    resolved = false;
                    continue;
                }

                behaviors.Add(behaviorType);
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
    ///     The first injected parameter type of the behavior method of <paramref name="kind" /> of a behavior class that does not resolve, or
    ///     <see langword="null" />: the generated pipeline context names these types, so one the host cannot resolve breaks the generated code
    ///     (OQ-47). Checked per kind (OQ-57): the class stays in the pipelines of its other kinds. Every parameter other than the message,
    ///     <c>next</c> and the token is injected; those three always resolve when the method's kind can be told.
    /// </summary>
    internal static ITypeSymbol? ServiceError(INamedTypeSymbol type, BehaviorKind kind, KeryxSymbols symbols) =>
        BehaviorShapes.CandidateOfKind(type, kind, symbols) is { } method
            ? method.Parameters
                    .Where(p => !symbols.IsCancellationToken(p.Type) && p.Type is not ITypeParameterSymbol && !MessageType.Classify(p.Type, symbols).IsMessage)
                    .Select(static p => FirstError(p.Type))
                    .FirstOrDefault(static e => e is not null)
            : null;
}
