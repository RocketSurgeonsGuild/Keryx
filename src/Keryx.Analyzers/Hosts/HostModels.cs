using System.Collections.Immutable;
using Keryx.Analyzers.Behaviors;
using Keryx.Analyzers.Handlers;
using Specular.Scanning;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     One handler as the host composes it into a pipeline: what it handles, how to call it and how to construct it. It holds only what the
///     host needs, in a form that does not depend on where the handler came from: the host's own handlers are found by its syntax pipeline,
///     referenced handlers are read from their manifests, and both are projected into the same record (<see cref="HostModelFactory" />), so
///     the emitter does not care which.
/// </summary>
/// <param name="HandlerClass">The handler class, fully qualified with <c>global::</c>.</param>
/// <param name="Kind">What the handler handles.</param>
/// <param name="MessageType">The request, stream request or notification parameter type, fully qualified, without a nullable annotation.</param>
/// <param name="MessageIsValueType">Whether the message type is a struct (unboxed on the generic path, KRX021).</param>
/// <param name="ResponseType">The request's <c>TResponse</c> or the stream request's <c>TItem</c>; <see langword="null" /> for a notification.</param>
/// <param name="StaticInvoker">Whether <c>InvokeAsync</c> is static: the handler has no injected parameters and is never constructed.</param>
/// <param name="StaticEntry">
///     Whether the handler is the host's own and has injected parameters, so the handler generator gives it the static entry
///     <c>__keryx_InvokeAsync</c> (owner decision 44): its pipeline context holds the handler's services, resolved once per scope, and
///     calls that entry, so the handler class is never constructed. A referenced handler with injected parameters has none: its context
///     constructs it with <c>new</c> (owner decision 42). <see langword="false" /> for a static invoker.
/// </param>
/// <param name="Services">The generated constructor's parameters (the handler's injected parameters), in order.</param>
/// <param name="Behaviors">
///     The behaviors that apply to a request or stream handler's pipeline: the union of the three scopes (per target, per assembly, global),
///     de-duplicated by behavior class, minus the host's <c>ExcludeBehavior</c> entries, in no particular order (ordinal by class). The
///     emitter sorts them (ADR 0001, Ordering). Empty for a notification handler, which has no behaviors of its own.
/// </param>
/// <param name="UsedExclusions">The <c>ExcludeBehavior</c> operands that removed a behavior from this pipeline (for KRX036).</param>
/// <param name="Order">The <c>[HandlerOrder]</c> of a notification handler.</param>
/// <param name="Deliveries">
///     For a notification handler, every concrete notification type the host knows that converts to the handler's parameter type (ADR 0001,
///     Polymorphic handlers), in no particular order (ordinal by type). Empty for request and stream request handlers.
/// </param>
/// <param name="ContextServices">
///     The distinct services a request or stream handler's pipeline context resolves once each and shares (<see cref="ContextServiceModel" />,
///     owner decisions 9 and 43): those its behavior methods inject and those the handler injects, in the stable order of
///     <see cref="PipelineBehaviors.CompareServices" />; the behaviors' <see cref="PipelineBehaviorModel.ServiceSlots" /> and the handler's
///     <paramref name="ServiceSlots" /> index into it. The unkeyed <c>IServiceProvider</c> is not among them
///     (<paramref name="ContextServiceProvider" />). Empty for a notification handler: a publish context shares the services of all its
///     handlers and behaviors (<see cref="NotificationPipeline.Services" />).
/// </param>
/// <param name="ContextServiceProvider">
///     Whether a behavior method of the pipeline, or the handler through its static entry, injects the unkeyed <c>IServiceProvider</c>, so
///     its pipeline context holds the provider it is built with in a <c>ServiceProvider</c> field (OQ-63); a referenced handler's
///     constructor receives the provider the context is built with directly. <see langword="false" /> for a notification handler.
/// </param>
/// <param name="ServiceSlots">
///     For each of <paramref name="Services" /> of a request or stream handler, the index of the context service it receives
///     (<paramref name="ContextServices" />), or <see cref="PipelineBehaviors.ServiceProviderSlot" /> for the unkeyed
///     <c>IServiceProvider</c>. Empty for a notification handler (its slots are its publish context's,
///     <see cref="NotificationHandlerPipeline.ServiceSlots" />) and for a static invoker.
/// </param>
internal sealed record HostHandlerModel(
    string HandlerClass,
    HandlerKind Kind,
    string MessageType,
    bool MessageIsValueType,
    string? ResponseType,
    bool StaticInvoker,
    bool StaticEntry,
    EquatableArray<ServiceParameterModel> Services,
    EquatableArray<PipelineBehaviorModel> Behaviors,
    EquatableArray<string> UsedExclusions,
    int Order,
    EquatableArray<HostNotificationModel> Deliveries = default,
    EquatableArray<ContextServiceModel> ContextServices = default,
    bool ContextServiceProvider = false,
    EquatableArray<int> ServiceSlots = default
);

/// <summary>
///     A concrete notification type the host knows (ADR 0001, Notifications): its publish strategy and its notification behaviors, composed
///     from the three scopes.
/// </summary>
/// <param name="Type">The notification type, fully qualified with <c>global::</c>.</param>
/// <param name="DisplayName">The name diagnostics show.</param>
/// <param name="IsValueType">Whether it is a struct (dispatched without boxing; boxed once for interface-typed handlers, KRX037).</param>
/// <param name="IsSealed">Whether no other type can derive from it (a sealed class or a struct).</param>
/// <param name="Parallel">Whether it carries <c>[PublishStrategy(PublishStrategy.Parallel)]</c>.</param>
/// <param name="Behaviors">
///     The notification behaviors: the type's <c>[Behavior]</c>s, its assembly's <c>[assembly: Behavior]</c>s (OQ-3) and the global ones,
///     de-duplicated, minus the host's exclusions, in no particular order (ordinal by class).
/// </param>
/// <param name="UsedExclusions">The <c>ExcludeBehavior</c> operands that removed a behavior from the publish pipeline (for KRX036).</param>
/// <param name="Location">Its first declaration when it is in source, for KRX037.</param>
/// <param name="ContextServices">
///     The distinct services its notification behaviors inject (<see cref="ContextServiceModel" />), in the stable order of
///     <see cref="PipelineBehaviors.CompareServices" />; the behaviors' <see cref="PipelineBehaviorModel.ServiceSlots" /> index into it. The
///     unkeyed <c>IServiceProvider</c> is not among them (<paramref name="ContextServiceProvider" />). Its publish pipeline context also shares
///     them with the services its handlers inject, which only the composition knows (<see cref="NotificationPipeline.Services" />).
/// </param>
/// <param name="ContextServiceProvider">
///     Whether a notification behavior injects the unkeyed <c>IServiceProvider</c>, so its publish pipeline context holds the provider it is
///     built with in a <c>ServiceProvider</c> field (OQ-63).
/// </param>
internal sealed record HostNotificationModel(
    string Type,
    string DisplayName,
    bool IsValueType,
    bool IsSealed,
    bool Parallel,
    EquatableArray<PipelineBehaviorModel> Behaviors,
    EquatableArray<string> UsedExclusions,
    LocationInfo? Location,
    EquatableArray<ContextServiceModel> ContextServices = default,
    bool ContextServiceProvider = false
);

/// <summary>
///     One behavior in a pipeline: what the emitter needs to call its behavior method and resolve its injected services, and what the sort
///     and KRX008 need to place it (ADR 0001, Behavior methods, Generated pipeline shape, Ordering).
/// </summary>
/// <param name="Class">The behavior class, fully qualified.</param>
/// <param name="Identity">The class's <c>typeof</c> operand: de-duplication, exclusions, edges and the ordinal sort name.</param>
/// <param name="Kind">The kind of its behavior method in this pipeline.</param>
/// <param name="Method">The behavior method's name (<c>Handle</c> or <c>HandleAsync</c>).</param>
/// <param name="TypeArguments">
///     The method's explicit type arguments, in declared order, fully qualified with nullable annotations; empty in the position of
///     <c>TNext</c>, which is the generated link.
/// </param>
/// <param name="Parameters">What each parameter receives, in declared order.</param>
/// <param name="Phase">Its <c>[BehaviorOrder]</c> phase (the method's, else the class's).</param>
/// <param name="Order">Its <c>[BehaviorOrder]</c> order.</param>
/// <param name="OrderOverridden">Whether the method declares ordering of its own, so KRX008 is checked for its kind on its own.</param>
/// <param name="Edges">Its <c>RunsAfter</c>/<c>RunsBefore</c> edges, in declaration order.</param>
/// <param name="Services">Its injected parameters, in declared order.</param>
/// <param name="Assembly">The assembly that declares it.</param>
/// <param name="InSource">Whether it is declared in the host's own source (KRX008 for in-source pairs is the analyzer's).</param>
/// <param name="ObliviousFit">
///     Whether the method fits only through a nullable-oblivious response (or item) type where it writes <c>TResponse?</c> (OQ-58), so its
///     call is emitted with nullable warnings disabled.
/// </param>
/// <param name="ServiceSlots">
///     For each of <paramref name="Services" />, the index of the pipeline context service it receives (the pipeline's
///     <c>ContextServices</c>), or <see cref="PipelineBehaviors.ServiceProviderSlot" /> for the unkeyed <c>IServiceProvider</c>, which
///     receives the context's <c>ServiceProvider</c> field; set by <see cref="PipelineBehaviors.ShareServices" />.
/// </param>
internal sealed record PipelineBehaviorModel(
    string Class,
    string Identity,
    BehaviorKind Kind,
    string Method,
    EquatableArray<string> TypeArguments,
    EquatableArray<BehaviorParameterRole> Parameters,
    int Phase,
    int Order,
    bool OrderOverridden,
    EquatableArray<BehaviorEdgeModel> Edges,
    EquatableArray<ServiceParameterModel> Services,
    string Assembly,
    bool InSource,
    bool ObliviousFit,
    EquatableArray<int> ServiceSlots = default
)
{
    /// <summary>What the sort compares.</summary>
    public OrderingKey Key => new(Identity, Phase, Order, [.. Edges.Select(static e => e.Edge)]);
}

/// <summary>
///     A <c>RunsAfter</c>/<c>RunsBefore</c> edge in a model, with where it was declared for KRX008.
/// </summary>
/// <param name="Edge">The edge.</param>
/// <param name="Location">The attribute that declared it, when it is in source.</param>
internal sealed record BehaviorEdgeModel(OrderingEdge Edge, LocationInfo? Location);

/// <summary>A <c>[GlobalBehavior]</c> the host applies, for KRX035: one per behavior class and kind it makes global.</summary>
/// <param name="Name">The behavior's display name.</param>
/// <param name="Kind">The kind it is global for.</param>
/// <param name="Assembly">The assembly that declares it.</param>
/// <param name="Location">The <c>[GlobalBehavior]</c> attribute, when it is in source.</param>
internal sealed record GlobalBehaviorModel(string Name, BehaviorKind Kind, string Assembly, LocationInfo? Location);

/// <summary>An <c>[assembly: ExcludeBehavior(typeof(X))]</c> or <c>[assembly: ExcludeHandler(typeof(X))]</c> entry of the host, for KRX036.</summary>
/// <param name="Operand">The excluded type's <c>typeof</c> operand, as <see cref="HostHandlerModel.UsedExclusions" /> lists it.</param>
/// <param name="Display">The excluded type's display name.</param>
/// <param name="Location">The attribute.</param>
internal sealed record ExclusionModel(string Operand, string Display, LocationInfo? Location);

/// <summary>
///     One injected parameter of a handler's generated constructor or of a behavior method, resolved by a pipeline context (ADR 0001,
///     Injected parameters).
/// </summary>
/// <param name="Type">The parameter type, fully qualified, with nullable annotations.</param>
/// <param name="ServiceType">
///     The service type to resolve: <paramref name="Type" /> without its top-level nullable reference annotation, because the
///     <c>GetRequiredService&lt;T&gt;</c> family constrains <c>T</c> to <c>notnull</c>.
/// </param>
/// <param name="ServiceKey">The <c>[FromKeyedServices]</c> key as a C# expression, or <see langword="null" />.</param>
/// <param name="DefaultValue">The default value as a C# expression, or <see langword="null" /> when the parameter is required.</param>
/// <param name="IsValueType">Whether the parameter type is a value type, including <c>Nullable&lt;T&gt;</c>.</param>
/// <param name="NullableUnderlyingType">
///     For a <c>Nullable&lt;T&gt;</c> parameter, <c>T</c>: a type pattern cannot name a nullable type, so an optional one is matched on
///     <c>T</c>.
/// </param>
/// <param name="IsServiceProvider">
///     Whether the parameter is <c>IServiceProvider</c>. Unkeyed, it receives the scope's provider itself, never resolved (OQ-63); keyed, it
///     is an ordinary keyed service.
/// </param>
internal sealed record ServiceParameterModel(
    string Type,
    string ServiceType,
    string? ServiceKey,
    string? DefaultValue,
    bool IsValueType,
    string? NullableUnderlyingType,
    bool IsServiceProvider
);

/// <summary>
///     One service a pipeline context resolves once and shares among the behavior methods that inject it (ADR 0001, Injected parameters):
///     identified by (<paramref name="ServiceType" />, <paramref name="ServiceKey" />). When any of its consumers is required it is resolved
///     as required (<c>GetRequiredService</c>/<c>GetRequiredKeyedService</c>) and every consumer receives that instance; when all of them
///     are optional it is resolved once with <c>GetService</c>/<c>GetKeyedService</c> into a nullable field, and each call site applies its
///     own parameter's default. The unkeyed <c>IServiceProvider</c> is not one of them: the context holds the provider it is built with in
///     its own <c>ServiceProvider</c> field (OQ-63). A keyed <c>IServiceProvider</c> is an ordinary keyed service. Its consumers are the
///     context's behavior methods and its handlers (owner decision 43): the host's own handlers receive it through their static entries,
///     referenced handlers through the constructor the context calls.
/// </summary>
/// <param name="ServiceType">The service type, fully qualified, without a top-level nullable reference annotation.</param>
/// <param name="ServiceKey">The <c>[FromKeyedServices]</c> key as a C# expression, or <see langword="null" />.</param>
/// <param name="IsValueType">Whether the service type is a value type, including <c>Nullable&lt;T&gt;</c>.</param>
/// <param name="NullableUnderlyingType">For a <c>Nullable&lt;T&gt;</c> service type, <c>T</c>.</param>
/// <param name="Required">Whether at least one consumer is required (has no default value), so the service is resolved as required.</param>
/// <param name="Held">
///     Whether the context holds it in a field: a behavior method or an own handler's static entry reads it on every call. When only the
///     constructors of referenced handlers take it, the context's constructor resolves it into a local and passes it to them.
/// </param>
internal sealed record ContextServiceModel(
    string ServiceType,
    string? ServiceKey,
    bool IsValueType,
    string? NullableUnderlyingType,
    bool Required,
    bool Held
)
{
    /// <summary>
    ///     The service a parameter injects, as a pipeline context shares it: required unless the parameter has a default value; held in a
    ///     field when <paramref name="held" /> (the consumer reads it on every call, not only in a constructor).
    /// </summary>
    public static ContextServiceModel For(ServiceParameterModel parameter, bool held) =>
        new(parameter.ServiceType, parameter.ServiceKey, parameter.IsValueType, parameter.NullableUnderlyingType, parameter.DefaultValue is null, held);

    /// <summary>Whether a parameter injects this service: the same service type and key.</summary>
    public bool Matches(ServiceParameterModel parameter) =>
        string.Equals(ServiceType, parameter.ServiceType, StringComparison.Ordinal) && string.Equals(ServiceKey, parameter.ServiceKey, StringComparison.Ordinal);
}

/// <summary>
///     What the host's syntax pipeline found for one handler candidate: enough to count its request as handled and to find the method again
///     in the composition step, which composes its pipeline with the behaviors and notification types of the host's references.
/// </summary>
/// <param name="HandledType">
///     The concrete request or stream request type the candidate takes, fully qualified, even when the candidate breaks a rule: a malformed
///     handler is reported by the analyzers, and KRX030 does not add a second error for its request. <see langword="null" /> for a
///     notification handler or when the candidate has no single concrete request parameter.
/// </param>
/// <param name="HandlerClass">The metadata name of the handler class (<c>Ns.Outer+Inner</c>).</param>
/// <param name="Location">The handler method's declaration, which tells it apart from other candidates of its class.</param>
internal sealed record HostHandlerEntry(string? HandledType, string HandlerClass, LocationInfo? Location);

/// <summary>
///     A request or stream request type declared in a reference of the host, for KRX030 (the host's own are KRX001's).
/// </summary>
/// <param name="Type">The type, fully qualified with <c>global::</c>.</param>
/// <param name="DisplayName">The name the diagnostic shows.</param>
/// <param name="Location">A source location, if any; without one KRX030 has no location (<see cref="Microsoft.CodeAnalysis.Location.None" />).</param>
internal sealed record HostRequestModel(string Type, string DisplayName, LocationInfo? Location);

/// <summary>
///     A host diagnostic, computed from the cached models and reported by the diagnostics output.
/// </summary>
/// <param name="Descriptor">The rule.</param>
/// <param name="Location">Where it is reported, or <see langword="null" /> for <see cref="Microsoft.CodeAnalysis.Location.None" />.</param>
/// <param name="Arguments">The message arguments.</param>
internal sealed record HostDiagnosticModel(Microsoft.CodeAnalysis.DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> Arguments);

/// <summary>
///     The compilation-wide inputs of the host generator.
/// </summary>
/// <param name="Enabled">
///     The compilation references Keryx, the project is a host (<c>build_property.KeryxHost</c> is <c>true</c>) and it compiles with C# 14
///     or later (KRX022). When false the host generator emits and reports nothing.
/// </param>
/// <param name="Namespace">The namespace of the generated host types (ADR 0001, Opting in).</param>
/// <param name="AssemblyName">The host's assembly name, for messages.</param>
/// <param name="UnsupportedTargetFramework">
///     The target framework KRX031 reports when it is older than <c>net10.0</c>, or <see langword="null" /> when it is supported.
/// </param>
internal sealed record HostSettings(bool Enabled, string Namespace, string AssemblyName, string? UnsupportedTargetFramework)
{
    public static HostSettings Disabled { get; } = new(false, "", "", null);
}

/// <summary>
///     What the host generator reads from the compilation itself (fed directly by the compilation, so this step always runs).
/// </summary>
/// <param name="ReferencesKeryx">The compilation references Keryx.</param>
/// <param name="AssemblyName">The assembly name.</param>
/// <param name="RuntimeAssemblyName">The name of the referenced runtime assembly used when the target framework properties are absent.</param>
/// <param name="RuntimeAssemblyVersion">Its version, or <see langword="null" /> when no runtime assembly is referenced.</param>
internal sealed record HostCompilationInfo(bool ReferencesKeryx, string AssemblyName, string? RuntimeAssemblyName, Version? RuntimeAssemblyVersion);

/// <summary>
///     The MSBuild properties the host generator reads (<c>build/Keryx.props</c> makes them compiler visible).
/// </summary>
/// <param name="IsHost">
///     <c>KeryxHost</c> is <c>true</c> (<c>build/Keryx.targets</c> defaults it for executables and test projects; unset is not a host).
/// </param>
/// <param name="RootNamespace"><c>RootNamespace</c>, or <see langword="null" /> when absent or empty.</param>
/// <param name="TargetFrameworkIdentifier"><c>TargetFrameworkIdentifier</c>, or <see langword="null" /> when absent or empty.</param>
/// <param name="TargetFrameworkVersion"><c>TargetFrameworkVersion</c>, or <see langword="null" /> when absent or empty.</param>
internal sealed record HostBuildProperties(bool IsHost, string? RootNamespace, string? TargetFrameworkIdentifier, string? TargetFrameworkVersion);

/// <summary>
///     Everything the host composes from its own source and its references (ADR 0001, How the host reads it), computed in one step that
///     resolves the references' manifests in the compilation. It holds no symbols, so the source output stays cached while it is unchanged.
/// </summary>
/// <param name="Settings">The host generator's settings.</param>
/// <param name="Handlers">
///     The handlers the host composes: its own, then the referenced ones its <c>ExcludeHandler</c> entries keep (they remove only
///     notification handlers; KRX039), each once. A request type handled in more than one assembly cannot come from generated manifests
///     (KRX023) and is dispatched by neither (OQ-60).
/// </param>
/// <param name="Notifications">
///     The concrete notification types declared in the host's source and in its references, with their notification behaviors, ordered by
///     type.
/// </param>
/// <param name="HandledRequests">
///     Every request and stream request type that has a handler in the composed set, malformed or excluded-by-error ones included, fully
///     qualified (KRX030).
/// </param>
/// <param name="ReferencedRequests">
///     The request and stream request types of the references that KRX030 covers (those declared in a referenced host included, OQ-16),
///     ordered by type.
/// </param>
/// <param name="GlobalBehaviors">The global behaviors the host applies (not excluded), own and referenced, for KRX035.</param>
/// <param name="BehaviorExclusions">The host's <c>ExcludeBehavior</c> entries, for KRX036.</param>
/// <param name="Diagnostics">
///     The diagnostics found while composing: KRX032, KRX033, KRX034, KRX038, KRX039, and KRX036 for <c>ExcludeHandler</c>.
/// </param>
internal sealed record HostModel(
    HostSettings Settings,
    EquatableArray<HostHandlerModel> Handlers,
    EquatableArray<HostNotificationModel> Notifications,
    EquatableArray<string> HandledRequests,
    EquatableArray<HostRequestModel> ReferencedRequests,
    EquatableArray<GlobalBehaviorModel> GlobalBehaviors,
    EquatableArray<ExclusionModel> BehaviorExclusions,
    EquatableArray<HostDiagnosticModel> Diagnostics
)
{
    /// <summary>A host that composes nothing (not a host, or the generator is off).</summary>
    public static HostModel Empty(HostSettings settings) =>
        new(
            settings,
            ImmutableArray<HostHandlerModel>.Empty,
            ImmutableArray<HostNotificationModel>.Empty,
            ImmutableArray<string>.Empty,
            ImmutableArray<HostRequestModel>.Empty,
            ImmutableArray<GlobalBehaviorModel>.Empty,
            ImmutableArray<ExclusionModel>.Empty,
            ImmutableArray<HostDiagnosticModel>.Empty
        );
}
