using Microsoft.CodeAnalysis;

namespace Keryx.Analyzers.Diagnostics;

/// <summary>
///     Every Keryx diagnostic (ADR 0001, Diagnostics and code fixes). IDs are grouped: KRX001-KRX029 per-assembly rules, KRX030-KRX039
///     host-only rules, KRX040-KRX049 attribute placement rules.
/// </summary>
internal static class Descriptors
{
    public const string Category = "Keryx";

    /// <summary>The README section every help link points into.</summary>
    public const string HelpLinkBase = "https://github.com/RocketSurgeonsGuild/Keryx/blob/main/Readme.md#";

    /// <summary>Diagnostic property: the KRX002 problem, <see cref="ModifiersReason" /> or <see cref="ReturnTypeReason" />.</summary>
    public const string ReasonProperty = "Reason";

    public const string ModifiersReason = "Modifiers";
    public const string ReturnTypeReason = "ReturnType";

    /// <summary>Diagnostic property on KRX004: <c>"true"</c> when the type lacks <c>partial</c>.</summary>
    public const string AddPartialProperty = "AddPartial";

    /// <summary>Diagnostic property on KRX004: <c>"true"</c> when the type must become <c>public</c>.</summary>
    public const string MakePublicProperty = "MakePublic";

    public static readonly DiagnosticDescriptor MissingHandler = new(
        "KRX001",
        "Request has no handler",
        "Request type '{0}' has no handler in this assembly",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every request and stream request needs exactly one handler. Add one, or mark the assembly [assembly: KeryxContracts] when the handlers live in another assembly.",
        helpLinkUri: HelpLinkBase + "krx001",
        customTags: WellKnownDiagnosticTags.CompilationEnd
    );

    public static readonly DiagnosticDescriptor InvalidHandlerSignature = new(
        "KRX002",
        "Handler is not private static, or returns a type its request kind does not allow",
        "Handler '{0}' {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A handler is a private static method. A request handler returns ValueTask<T>, Task<T> or T; an IRequest handler may also return ValueTask, Task or void (not async void); a stream request handler returns IAsyncEnumerable<T>; a notification handler returns ValueTask or Task.",
        helpLinkUri: HelpLinkBase + "krx002"
    );

    public static readonly DiagnosticDescriptor InvalidHandlerParameter = new(
        "KRX003",
        "Handler has no single concrete request or notification parameter",
        "Handler '{0}' {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A handler takes exactly one request or notification parameter. A request parameter is the concrete request type; a notification parameter may also be a base type or interface deriving from INotification.",
        helpLinkUri: HelpLinkBase + "krx003"
    );

    public static readonly DiagnosticDescriptor InvalidHandlerClass = new(
        "KRX004",
        "Handler class or a containing type is not partial, not public, or not a class",
        "'{0}' {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Keryx generates the other half of every handler class, so the class and every type containing it are partial and not file-local. Outside a host they are also public, so a host can use the handler; in a host they are at least internal, because generated code outside the class uses it. The handler class is a class or record class.",
        helpLinkUri: HelpLinkBase + "krx004"
    );

    public static readonly DiagnosticDescriptor DuplicateHandler = new(
        "KRX005",
        "Request has more than one handler",
        "Request type '{0}' has more than one handler in this assembly: {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A request or stream request has exactly one handler in the application.",
        helpLinkUri: HelpLinkBase + "krx005",
        customTags: WellKnownDiagnosticTags.CompilationEnd
    );

    /// <summary>KRX005 as the host reports it: handlers of one request type in more than one assembly of its composed set.</summary>
    public static readonly DiagnosticDescriptor DuplicateHandlerInHost = new(
        "KRX005",
        "Request has more than one handler",
        "Request type '{0}' has more than one handler in this host: {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A request or stream request has exactly one handler in the application. To replace a handler from a referenced assembly, exclude it with [assembly: ExcludeHandler(typeof(...))] and provide your own.",
        helpLinkUri: HelpLinkBase + "krx005"
    );

    public static readonly DiagnosticDescriptor MultipleHandlersInClass = new(
        "KRX006",
        "Class contains more than one handler",
        "'{0}' contains more than one handler method; give each handler its own class",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A handler class contains exactly one handler method, counting direct members only. Group handlers with nested classes.",
        helpLinkUri: HelpLinkBase + "krx006"
    );

    public static readonly DiagnosticDescriptor InvalidHandlerConstructor = new(
        "KRX007",
        "Handler class declares a constructor, or its base type has no accessible parameterless constructor",
        "'{0}' {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Keryx generates the constructor of a handler class from the handler's injected parameters. The class declares no instance constructor (a record primary constructor counts as one). With injected parameters a host constructs the class, so it is not abstract, it and its base types declare no required members, and its base type has an accessible parameterless constructor.",
        helpLinkUri: HelpLinkBase + "krx007"
    );

    public static readonly DiagnosticDescriptor InvalidBehaviorOrdering = new(
        "KRX008",
        "Behavior ordering has a cycle, or RunsAfter/RunsBefore contradicts phase or order",
        "'{0}' {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Behaviors are ordered by phase, then order, then RunsAfter/RunsBefore inside one (phase, order). An edge between behaviors whose phase or order already puts them the other way round contradicts them, and edges that form a cycle cannot be satisfied. Each behavior pair is checked once: by the analyzer when both behaviors are in source, by the host when one is referenced.",
        helpLinkUri: HelpLinkBase + "krx008",
        customTags: WellKnownDiagnosticTags.CompilationEnd
    );

    public static readonly DiagnosticDescriptor BehaviorDoesNotFit = new(
        "KRX009",
        "Attached behavior does not fit its target",
        "Behavior '{0}' does not fit '{1}': {2}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A behavior attached with [Behavior] to a request or stream handler method or a notification type must be a behavior class with a behavior method of the target's kind; an open method's constraints must hold for the target's types, and a closed method must be closed over exactly the target's types. Assembly and global behaviors that do not fit are skipped silently.",
        helpLinkUri: HelpLinkBase + "krx009"
    );

    public static readonly DiagnosticDescriptor ConflictingMessageInterfaces = new(
        "KRX011",
        "Type implements conflicting request or notification interfaces",
        "'{0}' implements {1}; a type is exactly one request, stream request or notification",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A request implements exactly one closing of IRequest<> or IStreamRequest<>, never both, and never together with INotification. Dispatch reinterprets the response type, which is only safe with one closing.",
        helpLinkUri: HelpLinkBase + "krx011"
    );

    public static readonly DiagnosticDescriptor UnsupportedTypeShape = new(
        "KRX012",
        "Request, notification or handler class has an unsupported shape",
        "'{0}' {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Requests, stream requests, notifications and handler classes have no type parameters and are not nested in generic types. A ref struct cannot be a request or notification. Use a generic abstract base with closed subtypes instead.",
        helpLinkUri: HelpLinkBase + "krx012"
    );

    public static readonly DiagnosticDescriptor InvalidHandlerParameterKind = new(
        "KRX013",
        "Handler parameter is ref, in, out or params, or there is more than one CancellationToken",
        "Handler parameter '{0}' {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Handler and behavior method parameters are passed by value. A handler or behavior method takes at most one CancellationToken. An injected service is stored in a field (of the handler class, or of the pipeline context for a behavior method), so it cannot be a ref struct or a pointer.",
        helpLinkUri: HelpLinkBase + "krx013"
    );

    /// <summary>KRX013 on a behavior method parameter.</summary>
    public static readonly DiagnosticDescriptor InvalidBehaviorParameterKind = new(
        "KRX013",
        "Behavior method parameter is ref, in, out or params, or there is more than one CancellationToken",
        "Parameter '{0}' of behavior method '{1}' {2}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Handler and behavior method parameters are passed by value. A handler or behavior method takes at most one CancellationToken. An injected service is stored in a field (of the handler class, or of the pipeline context for a behavior method), so it cannot be a ref struct or a pointer.",
        helpLinkUri: HelpLinkBase + "krx013"
    );

    public static readonly DiagnosticDescriptor StaticHandlerClassWithDependencies = new(
        "KRX014",
        "Static handler class has injected parameters",
        "'{0}' is static, but its handler injects {1}; remove 'static' so Keryx can generate a constructor",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A static handler class is allowed only when the handler has no injected parameters, because the injected services are passed to a generated constructor.",
        helpLinkUri: HelpLinkBase + "krx014"
    );

    public static readonly DiagnosticDescriptor ReservedHandlerMember = new(
        "KRX015",
        "Handler class declares a member name Keryx generates",
        "'{0}' declares '{1}', a name Keryx generates in handler classes",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Keryx generates InvokeAsync and fields named __keryx_<parameter> in every handler class.",
        helpLinkUri: HelpLinkBase + "krx015"
    );

    /// <summary>The KRX016 <c>{1}</c> argument outside a host.</summary>
    public const string Public = "public";

    /// <summary>The KRX016 <c>{1}</c> argument in a host (OQ-24).</summary>
    public const string AtLeastInternal = "at least internal";

    public static readonly DiagnosticDescriptor NotEffectivelyPublic = new(
        "KRX016",
        "Type a pipeline needs is not public",
        "'{0}' must be {1}, because {2}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Outside a host, every request, response, notification, behavior class and behavior method that a host's pipeline uses, and every type a handler class's public constructor and invoker mention, must be public, as must the types containing them. In a host, every type a handler's signature mentions, and every behavior class and method its pipelines use, must be at least internal and not file-local, because the generated mediator and AddKeryx() use them from outside their containing types.",
        helpLinkUri: HelpLinkBase + "krx016"
    );

    public static readonly DiagnosticDescriptor ResponseNullabilityMismatch = new(
        "KRX017",
        "Handler response differs from the request's response only in nullability",
        "Handler '{0}' returns '{1}', but request '{2}' responds with '{3}'",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The response type of a handler matches the request's TResponse exactly, including nullability.",
        helpLinkUri: HelpLinkBase + "krx017"
    );

    public static readonly DiagnosticDescriptor InvalidServiceKey = new(
        "KRX018",
        "Keyed service key is not a compile-time constant",
        "Parameter '{0}' {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[FromKeyedServices(key)] on a handler or behavior method parameter needs a compile-time constant key. The parameterless (InheritKey) form is not supported.",
        helpLinkUri: HelpLinkBase + "krx018"
    );

    public static readonly DiagnosticDescriptor InvalidBehaviorShape = new(
        "KRX019",
        "Behavior class has an unsupported shape",
        "Behavior '{0}' {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A behavior is a non-generic static class with one or more kind attributes ([RequestBehavior], [StreamBehavior], [NotificationBehavior]) that declares exactly one static Handle or HandleAsync behavior method per kind it carries. The method takes the message, a 'next' method type parameter constrained 'struct' and the kind's next interface, an optional CancellationToken and injected services, returns the kind's type, and is fully open (with the message type parameter constrained to the kind's message interface) or fully closed (generic only in 'next', with 'next' carrying exactly the message's response or item type).",
        helpLinkUri: HelpLinkBase + "krx019"
    );

    public static readonly DiagnosticDescriptor BoxedStructRequest = new(
        "KRX021",
        "Struct request is boxed when sent through ISender",
        "Struct request '{0}' is boxed when it is sent through {1}; only the host's typed overload avoids the box",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "ISender.Send and ISender.Stream take the request as an interface, which boxes a struct. The host's generated KeryxMediator has a typed overload per request that does not.",
        helpLinkUri: HelpLinkBase + "krx021"
    );

    public static readonly DiagnosticDescriptor LanguageVersionTooLow = new(
        "KRX022",
        "Keryx needs C# 14 or later",
        "This project compiles with C# {0}, but Keryx needs C# 14 or later; remove the LangVersion setting (the Keryx package then defaults it to 'latest') or set it to 14 or later",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every project that references Keryx compiles with C# 14 or later, the default language of the .NET 10 SDK that Keryx requires; generated code may use any C# 14 feature. The Keryx generators emit nothing in a project below that.",
        helpLinkUri: HelpLinkBase + "krx022",
        customTags: WellKnownDiagnosticTags.CompilationEnd
    );

    public static readonly DiagnosticDescriptor UnhandledRequestInHost = new(
        "KRX030",
        "Request has no handler in the host",
        "Request type '{0}' has no handler in this host",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every request and stream request type a host can see, including those declared in a referenced host, needs exactly one handler in the host's composed set: the host's own handlers and the handlers of the assemblies it references. A request handled outside the host is named with [assembly: ExternallyHandled(typeof(X))], which turns the rule off for that type; the severity can also be changed in .editorconfig.",
        helpLinkUri: HelpLinkBase + "krx030"
    );

    public static readonly DiagnosticDescriptor HostTargetFrameworkTooOld = new(
        "KRX031",
        "Host targets a framework older than net10.0",
        "'{0}' is a Keryx host ([assembly: KeryxHost]), so it must target net10.0 or newer, but it targets {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A host's generated mediator uses Unsafe.As and FrozenDictionary, so a host targets net10.0 or newer. Libraries with requests and handlers may target netstandard2.0. The host generator emits nothing in a host on an older framework.",
        helpLinkUri: HelpLinkBase + "krx031"
    );

    public static readonly DiagnosticDescriptor NewerManifestFormat = new(
        "KRX032",
        "Referenced assembly has a newer Keryx manifest",
        "Assembly '{0}' carries a Keryx manifest of format version {1}, but the Keryx in this project reads format versions up to {2}; upgrade Keryx in this project",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The referenced assembly was built with a newer Keryx, whose manifest this host cannot read. Its handlers, behaviors and requests are left out until Keryx is upgraded in the host.",
        helpLinkUri: HelpLinkBase + "krx032"
    );

    public static readonly DiagnosticDescriptor MissingManifest = new(
        "KRX033",
        "Referenced assembly uses Keryx but has no manifest",
        "Assembly '{0}' references Keryx but has no Keryx manifest, so the Keryx generator did not run there and its handlers are not part of this host; reference the Keryx package directly from that project",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Analyzers do not flow through a ProjectReference, so every project that declares requests or handlers references the Keryx package itself. Without it the generator does not run there and the host cannot see its handlers.",
        helpLinkUri: HelpLinkBase + "krx033"
    );

    public static readonly DiagnosticDescriptor UnresolvedManifestType = new(
        "KRX034",
        "Type in a Keryx manifest cannot be resolved",
        "Type '{0}', which the Keryx manifest of '{1}' needs, cannot be resolved or is not accessible from this host; reference assembly '{2}'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A referenced handler or behavior names a type from an assembly the host does not reference (for example a contracts assembly hidden with PrivateAssets), such as a handler's response or constructor parameter type or the injected parameter type of a behavior method. The handler or behavior is left out of the host until the assembly is referenced.",
        helpLinkUri: HelpLinkBase + "krx034"
    );

    /// <summary>
    ///     KRX034 for a referenced handler whose manifest entry resolves but no longer describes a handler the host can compose with the types it
    ///     resolves (typically a library built against another version of a contracts assembly than the host references; OQ-47).
    /// </summary>
    public static readonly DiagnosticDescriptor ManifestEntryDoesNotFit = new(
        "KRX034",
        "Type in a Keryx manifest cannot be resolved",
        "Handler '{0}' in the Keryx manifest of '{1}' does not fit the types this host resolves: {2}; build '{1}' against the assemblies this host references",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A referenced handler's manifest entry names types that resolve in the host, but not to the shapes the handler was built against (for example a request that became a stream request in a newer contracts assembly). The handler is left out of the host until its assembly is rebuilt against the same references.",
        helpLinkUri: HelpLinkBase + "krx034"
    );

    public static readonly DiagnosticDescriptor GlobalBehaviorApplied = new(
        "KRX035",
        "Global behavior applies in this host",
        "Global behavior '{0}' from assembly '{1}' applies in this host",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Every [GlobalBehavior] the host sees applies to every matching target. Remove one with [assembly: ExcludeBehavior(typeof(...))].",
        helpLinkUri: HelpLinkBase + "krx035"
    );

    public static readonly DiagnosticDescriptor UnusedExclusion = new(
        "KRX036",
        "An exclusion or ExternallyHandled has no effect",
        "[assembly: {0}(typeof({1}))] has no effect, because {2}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An exclusion removes a handler or behavior the host would otherwise use, and ExternallyHandled turns KRX030 off for a request the host does not handle. One that names nothing the host uses, a request the host handles, or a type that is not a request or stream request is probably a mistake.",
        helpLinkUri: HelpLinkBase + "krx036"
    );

    public static readonly DiagnosticDescriptor BoxedStructNotification = new(
        "KRX037",
        "Struct notification is boxed for interface-typed handlers",
        "Struct notification '{0}' is boxed once per Publish, because handlers typed as an interface or INotification receive it: {1}",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A handler whose parameter is an interface or INotification cannot receive a struct without boxing it. The host boxes the notification once per Publish and reuses that box for every such handler. Type the handler with the struct itself to avoid it.",
        helpLinkUri: HelpLinkBase + "krx037"
    );

    public static readonly DiagnosticDescriptor MisplacedHandlerOrder = new(
        "KRX040",
        "[HandlerOrder] on a method that is not a notification handler",
        "[HandlerOrder] on '{0}' has no effect, because it is not a notification handler",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "[HandlerOrder] orders the handlers of one notification. On any other method it does nothing.",
        helpLinkUri: HelpLinkBase + "krx040"
    );

    public static readonly DiagnosticDescriptor MisplacedPublishStrategy = new(
        "KRX041",
        "[PublishStrategy] on a type that is not a notification",
        "[PublishStrategy] on '{0}' has no effect, because it is not a notification",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "[PublishStrategy] chooses how a notification's handlers run. On any other type it does nothing.",
        helpLinkUri: HelpLinkBase + "krx041"
    );

    public static readonly DiagnosticDescriptor MisplacedBehaviorAttribute = new(
        "KRX042",
        "[Behavior] or [GlobalBehavior] where it has no effect",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "[Behavior] attaches a behavior to a request or stream handler method, a notification type or the assembly; on any other class, struct or method, including a notification handler method (notification behaviors wrap the whole publish), it does nothing. [GlobalBehavior] marks a behavior class (a class with a behavior kind attribute) or one of its behavior methods.",
        helpLinkUri: HelpLinkBase + "krx042"
    );

    public static readonly DiagnosticDescriptor HostOnlyAttributeOutsideHost = new(
        "KRX043",
        "Host-only attribute outside a host",
        "[assembly: {0}] has no effect, because this assembly is not a host ([assembly: KeryxHost])",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "[assembly: ExternallyHandled], [assembly: ExcludeHandler] and [assembly: ExcludeBehavior] are read only by a host. In an assembly without [assembly: KeryxHost] they do nothing.",
        helpLinkUri: HelpLinkBase + "krx043"
    );

    /// <summary>The KRX036 reason for an exclusion that removes nothing.</summary>
    public static string NotPartOfAnyPipeline(string display) => $"'{display}' is not part of any pipeline in this host";

    /// <summary>The KRX036 reason for <c>ExternallyHandled</c> naming a request the host handles.</summary>
    public static string HandledByThisHost(string display) => $"'{display}' has a handler in this host";

    /// <summary>The KRX036 reason for <c>ExternallyHandled</c> naming a type that is not a request or stream request.</summary>
    public static string NotARequest(string display) => $"'{display}' is not a request or stream request";
}
