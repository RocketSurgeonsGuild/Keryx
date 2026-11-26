using System.Collections.Immutable;
using System.Text;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Specular.Scanning;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     Projects the host's compilation, build properties and syntax into the host generator's equatable models.
/// </summary>
internal static class HostModelFactory
{
    /// <summary>The runtime assembly KRX031 falls back to when the target framework properties are absent (ADR 0001, Which projects may target what).</summary>
    private const string SystemRuntime = "System.Runtime";

    /// <summary>The oldest framework a host may target (KRX031).</summary>
    private static readonly Version MinimumFramework = new(10, 0);

    public static HostCompilationInfo CreateCompilationInfo(Compilation compilation)
    {
        if (KeryxSymbols.Create(compilation) is null)
        {
            return new(false, compilation.AssemblyName ?? "", null, null);
        }

        var runtime = compilation.ReferencedAssemblyNames.FirstOrDefault(static identity => identity.Name == SystemRuntime)
         ?? compilation.ObjectType.ContainingAssembly?.Identity;
        return new(true, compilation.AssemblyName ?? "", runtime?.Name, runtime?.Version);
    }

    public static HostBuildProperties ReadBuildProperties(AnalyzerConfigOptionsProvider options) =>
        new(
            KeryxBuildProperties.IsHost(options.GlobalOptions),
            KeryxBuildProperties.Get(options.GlobalOptions, "RootNamespace"),
            KeryxBuildProperties.Get(options.GlobalOptions, "TargetFrameworkIdentifier"),
            KeryxBuildProperties.Get(options.GlobalOptions, "TargetFrameworkVersion")
        );

    public static HostSettings CreateSettings(HostCompilationInfo compilation, HostBuildProperties properties, bool languageVersionSupported) =>
        compilation.ReferencesKeryx && properties.IsHost && languageVersionSupported
            ? new(
                true,
                Namespace(properties.RootNamespace, compilation.AssemblyName),
                compilation.AssemblyName,
                UnsupportedTargetFramework(properties, compilation)
            )
            : HostSettings.Disabled;

    /// <summary>
    ///     The namespace of the generated host types: <c>RootNamespace</c>, falling back to the assembly name, sanitised into a valid namespace
    ///     (ADR 0001, Opting in): characters that are not valid in an identifier become <c>_</c>, a segment that starts with a digit gets a
    ///     <c>_</c> prefix, an empty segment becomes <c>_</c> and a keyword is escaped with <c>@</c>.
    /// </summary>
    public static string Namespace(string? rootNamespace, string assemblyName)
    {
        var name = string.IsNullOrWhiteSpace(rootNamespace) ? assemblyName : rootNamespace!.Trim();
        var segments = name.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = Segment(segments[i]);
        }

        return string.Join(".", segments);

        static string Segment(string segment)
        {
            var builder = new StringBuilder(segment.Length + 1);
            foreach (var character in segment)
            {
                builder.Append(SyntaxFacts.IsIdentifierPartCharacter(character) ? character : '_');
            }

            if (builder.Length == 0 || !SyntaxFacts.IsIdentifierStartCharacter(builder[0]))
            {
                builder.Insert(0, '_');
            }

            var identifier = builder.ToString();
            return SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ? "@" + identifier : identifier;
        }
    }

    /// <summary>
    ///     The framework KRX031 reports, or <see langword="null" /> when the host targets <c>net10.0</c> or newer. Reads
    ///     <c>TargetFrameworkIdentifier</c>/<c>TargetFrameworkVersion</c>; when they are absent (for example in a test harness), the version
    ///     of the referenced <c>System.Runtime</c> (or, without one, of the core library: <c>System.Private.CoreLib</c> passes like
    ///     <c>System.Runtime</c>) decides.
    /// </summary>
    public static string? UnsupportedTargetFramework(HostBuildProperties properties, HostCompilationInfo compilation)
    {
        return properties is { TargetFrameworkIdentifier: { } identifier, TargetFrameworkVersion: { } versionText }
            ? string.Equals(identifier, ".NETCoreApp", StringComparison.OrdinalIgnoreCase)
             && Version.TryParse(versionText.TrimStart('v', 'V'), out var version)
             && version >= MinimumFramework
                ? null
                : $"{identifier},Version={versionText}"
            : compilation switch
            {
                { RuntimeAssemblyName: SystemRuntime or "System.Private.CoreLib", RuntimeAssemblyVersion: { } runtimeVersion } when runtimeVersion >= MinimumFramework => null,
                { RuntimeAssemblyName: { } name, RuntimeAssemblyVersion: { } runtimeVersion } =>
                    $"a framework whose {name} is version {runtimeVersion.ToString(2)}",
                _ => "a framework without System.Runtime",
            };
    }

    /// <summary>
    ///     The syntax predicate of the host's notification pipeline: a class, struct or record declaration with a base list (a type without one
    ///     implements no interface).
    /// </summary>
    public static bool IsNotificationCandidateSyntax(SyntaxNode node) =>
        node is TypeDeclarationSyntax { BaseList: not null } and ( ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax );

    /// <summary>
    ///     The metadata name of the concrete notification type a declaration declares, or nothing (ADR 0001, Notifications). The host knows
    ///     every notification type declared in its source that generated code can reach, so its per-publish pipeline runs even when it has no
    ///     handlers (OQ-18); the composition step resolves the name and composes its per-publish behaviors.
    /// </summary>
    public static string? CreateNotification(GeneratorSyntaxContext context, CancellationToken cancellationToken) =>
        context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken) is INamedTypeSymbol type
     && KeryxSymbols.Create(context.SemanticModel.Compilation) is { } symbols
     && MessageType.Classify(type, symbols) is { IsNotificationOnly: true, IsConcrete: true, IsUnsupportedShape: false }
            ? ScanSymbolNames.GetFullMetadataName(type)
            : null;

    /// <summary>
    ///     What the host needs from one handler candidate (see <see cref="HostHandlerEntry" />), or nothing when the method is not a candidate.
    /// </summary>
    public static HostHandlerEntry? CreateHandler(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken) is not IMethodSymbol method
         || KeryxSymbols.Create(context.SemanticModel.Compilation) is not { } symbols
         || !HandlerAnalysis.IsCandidate(method, symbols))
        {
            return null;
        }

        var handledType = HandlerAnalysis.HandledRequestType(method, symbols) is { } requestType ? CSharpText.TypeOf(requestType) : null;
        return new(handledType, ScanSymbolNames.GetFullMetadataName(method.ContainingType), LocationInfo.From(method.Locations.FirstOrDefault()));
    }

    /// <summary>
    ///     The handler method an entry of the syntax pipeline stands for, found again in the compilation by its class and location, or
    ///     <see langword="null" /> when it is gone.
    /// </summary>
    public static IMethodSymbol? FindHandler(HostHandlerEntry entry, KeryxSymbols symbols)
    {
        if (symbols.Compilation.Assembly.GetTypeByMetadataName(entry.HandlerClass) is not { } type)
        {
            return null;
        }

        foreach (var method in HandlerAnalysis.GetCandidates(type, symbols))
        {
            if (Equals(LocationInfo.From(method.Locations.FirstOrDefault()), entry.Location))
            {
                return method;
            }
        }

        return null;
    }

    /// <summary>
    ///     The host's model of one of its own handlers, or <see langword="null" /> when the handler generator generates nothing for it in a host
    ///     (a rule is broken, or the class or its signature is narrower than <c>internal</c>; OQ-24, OQ-29).
    /// </summary>
    public static HostHandlerModel? CreateHandler(IMethodSymbol method, BehaviorCatalog catalog)
    {
        var symbols = catalog.Symbols;
        if (HandlerModelFactory.Create(method, symbols) is not { } model
         || model.ClassAccess == AccessLevel.Restricted
         || model.SignatureAccess == AccessLevel.Restricted)
        {
            return null;
        }

        ITypeSymbol? messageType = null;
        var services = ImmutableArray.CreateBuilder<ServiceParameterModel>();
        for (var i = 0; i < model.Parameters.Count; i++)
        {
            var parameter = method.Parameters[i];
            switch (model.Parameters[i].Role)
            {
                case ParameterRole.Message:
                    messageType = parameter.Type;
                    break;
                case ParameterRole.Injected:
                    services.Add(Service(parameter.Type, model.Parameters[i].ServiceKey, model.Parameters[i].DefaultValue));
                    break;
                case ParameterRole.CancellationToken or ParameterRole.Invalid:
                    break;
                default:
                    break;
            }
        }

        if (messageType is null)
        {
            return null;
        }

        var classified = MessageType.Classify(messageType, symbols);
        var responseType = model.Kind == HandlerKind.Stream ? classified.ItemType : classified.ResponseType;
        var (behaviors, usedExclusions) = PipelineBehaviors.Target(model.Kind, messageType, responseType) is { } target
            ? PipelineBehaviors.For(AttachedBehaviors(method, symbols), method.ContainingAssembly, target, catalog)
            : (ImmutableArray<PipelineBehaviorModel>.Empty, ImmutableArray<string>.Empty);
        var deliveries = model.Kind == HandlerKind.Notification
            ? Deliveries(messageType, catalog)
            : ImmutableArray<HostNotificationModel>.Empty;

        return new(
            model.HandlerClass,
            model.Kind,
            CSharpText.TypeOf(messageType),
            messageType.IsValueType,
            messageType.IsValueType || messageType.IsSealed,
            model.ResponseType,
            model.StaticInvoker,
            services.ToImmutable(),
            behaviors,
            usedExclusions,
            model.Order,
            deliveries
        );
    }

    /// <summary>
    ///     The host's model of a referenced handler, from its manifest entry (ADR 0001, Manifest): what it handles and its attached behaviors
    ///     come from <c>[KeryxHandler]</c>, its response type from the request type, its injected parameters from the generated public
    ///     constructor (which copies <c>[FromKeyedServices]</c> and default values). <see langword="null" />, with the reason in
    ///     <paramref name="problem" />, when the entry does not describe a handler the host can compose (a kind that does not fit the target as
    ///     the host resolves it, or not exactly one public constructor for a handler with injected parameters); the host reports it (KRX034).
    /// </summary>
    public static HostHandlerModel? CreateReferencedHandler(
        INamedTypeSymbol handlerClass,
        ITypeSymbol target,
        HandlerKind kind,
        ImmutableArray<ITypeSymbol> attached,
        int order,
        bool staticInvoker,
        BehaviorCatalog catalog,
        out string? problem
    )
    {
        var classified = MessageType.Classify(target, catalog.Symbols);
        var fits = kind switch
        {
            HandlerKind.Request => classified is { IsRequest: true, IsConcrete: true, IsUnsupportedShape: false },
            HandlerKind.Stream => classified is { IsStream: true, IsConcrete: true, IsUnsupportedShape: false },
            HandlerKind.Notification => classified is { IsNotificationOnly: true, IsUnsupportedShape: false },
            _ => false,
        };
        if (!fits)
        {
            problem = $"the manifest describes {KindText(kind)} of '{target.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}', but that type is not {MessageText(kind)} this host can dispatch";
            return null;
        }

        var services = ImmutableArray.CreateBuilder<ServiceParameterModel>();
        if (!staticInvoker)
        {
            if (handlerClass.InstanceConstructors.Where(static c => c.DeclaredAccessibility == Accessibility.Public).ToArray() is not [var constructor])
            {
                problem = "the class does not have exactly one public constructor for the host to call";
                return null;
            }

            services.AddRange(constructor.Parameters.Select(static p => Service(p.Type, HandlerModelFactory.ServiceKey(p), CSharpText.DefaultValue(p))));
        }

        var responseType = kind == HandlerKind.Stream ? classified.ItemType : classified.ResponseType;
        var (behaviors, usedExclusions) = PipelineBehaviors.Target(kind, target, responseType) is { } pipelineTarget
            ? PipelineBehaviors.For(attached, handlerClass.ContainingAssembly, pipelineTarget, catalog)
            : (ImmutableArray<PipelineBehaviorModel>.Empty, ImmutableArray<string>.Empty);
        var deliveries = kind == HandlerKind.Notification
            ? Deliveries(target, catalog)
            : ImmutableArray<HostNotificationModel>.Empty;

        problem = null;
        return new(
            CSharpText.Type(handlerClass),
            kind,
            CSharpText.TypeOf(target),
            target.IsValueType,
            target.IsValueType || target.IsSealed,
            responseType is null ? null : CSharpText.Type(responseType),
            staticInvoker,
            services.ToImmutable(),
            behaviors,
            usedExclusions,
            kind == HandlerKind.Notification ? order : 0,
            deliveries
        );
    }

    /// <summary>
    ///     KRX009 as the host checks it for a referenced handler (ADR 0001, Diagnostics: KRX009 for behaviors a referenced handler's manifest
    ///     attaches): each behavior its manifest attaches (<c>KeryxHandler.Behaviors</c>; a notification handler attaches none) that does not
    ///     fit the request or stream request as the host resolves it, with the analyzer's reasons. A behavior class the behavior rules reject
    ///     is the library's KRX019 and is not reported again.
    /// </summary>
    public static IEnumerable<(ITypeSymbol Behavior, string Reason)> UnfitAttachedBehaviors(
        ImmutableArray<ITypeSymbol> attached,
        ITypeSymbol target,
        HandlerKind kind,
        BehaviorCatalog catalog
    )
    {
        var classified = MessageType.Classify(target, catalog.Symbols);
        if (PipelineBehaviors.Target(kind, target, kind == HandlerKind.Stream ? classified.ItemType : classified.ResponseType) is not { } pipelineTarget)
        {
            return [];
        }

        var unfit = new List<(ITypeSymbol, string)>();
        _ = PipelineBehaviors.Compose(attached, [], pipelineTarget, catalog, (behavior, reason) => unfit.Add((behavior, reason)));
        return unfit;
    }

    private static string KindText(HandlerKind kind) =>
        kind switch
        {
            HandlerKind.Request => "a request handler",
            HandlerKind.Stream => "a stream request handler",
            HandlerKind.Notification => "a notification handler",
            _ => "a handler",
        };

    private static string MessageText(HandlerKind kind) =>
        kind switch
        {
            HandlerKind.Request => "a request",
            HandlerKind.Stream => "a stream request",
            HandlerKind.Notification => "a notification",
            _ => "a message",
        };

    /// <summary>The behaviors attached to a handler method with <c>[Behavior]</c>, in declaration order.</summary>
    private static ImmutableArray<ITypeSymbol> AttachedBehaviors(IMethodSymbol handler, KeryxSymbols symbols)
    {
        var attached = ImmutableArray.CreateBuilder<ITypeSymbol>();
        foreach (var attribute in handler.GetAttributes())
        {
            if (KeryxSymbols.IsAttribute(attribute, symbols.BehaviorAttribute)
             && attribute.ConstructorArguments is [{ Kind: TypedConstantKind.Type, Value: ITypeSymbol type }])
            {
                attached.Add(type);
            }
        }

        return attached.ToImmutable();
    }

    /// <summary>
    ///     The concrete notification types a notification handler receives (ADR 0001, Polymorphic handlers): every notification type the host
    ///     knows (<see cref="BehaviorCatalog.KnownNotifications" />: declared in its source, taken by its own or its referenced handlers, or
    ///     declared in its references; OQ-42) that converts to the handler's parameter type (identity, implicit reference or boxing
    ///     conversion).
    /// </summary>
    private static ImmutableArray<HostNotificationModel> Deliveries(ITypeSymbol parameterType, BehaviorCatalog catalog)
    {
        var deliveries = new SortedDictionary<string, HostNotificationModel>(StringComparer.Ordinal);
        foreach (var notification in catalog.KnownNotifications)
        {
            if (catalog.Notification(notification) is { } model
             && !deliveries.ContainsKey(model.Type)
             && BehaviorCatalog.Converts(notification, parameterType, catalog.Compilation))
            {
                deliveries.Add(model.Type, model);
            }
        }

        return [.. deliveries.Values];
    }

    /// <summary>One injected parameter of a handler constructor or a behavior method, as generated code resolves it.</summary>
    internal static ServiceParameterModel Service(ITypeSymbol type, string? serviceKey, string? defaultValue)
    {
        var serviceType = type.IsReferenceType ? type.WithNullableAnnotation(NullableAnnotation.NotAnnotated) : type;
        var underlying = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : null;
        return new(
            CSharpText.Type(type),
            CSharpText.Type(serviceType),
            serviceKey,
            defaultValue,
            type.IsValueType,
            underlying is null ? null : CSharpText.Type(underlying),
            type is INamedTypeSymbol { Name: "IServiceProvider", Arity: 0, ContainingType: null, ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } }
        );
    }
}
