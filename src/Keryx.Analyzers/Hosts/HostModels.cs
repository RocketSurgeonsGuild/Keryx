using Keryx.Analyzers.Handlers;
using Specular.Scanning;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     One handler as the host composes it into a pipeline: what it handles, how to call it and how to construct it. It holds only what the
///     host needs, in a form that does not depend on where the handler came from: the host's own handlers are projected from its syntax
///     pipeline (<see cref="HostModelFactory" />), and referenced handlers will be projected from their manifests (Phase 7) into the same
///     record, so the emitter does not care which.
/// </summary>
/// <param name="HandlerClass">The handler class, fully qualified with <c>global::</c>.</param>
/// <param name="Kind">What the handler handles.</param>
/// <param name="MessageType">The request, stream request or notification parameter type, fully qualified, without a nullable annotation.</param>
/// <param name="MessageIsValueType">Whether the message type is a struct (unboxed on the generic path, KRX021).</param>
/// <param name="MessageIsSealed">
///     Whether no other type can derive from the message type (a sealed class or a struct). A typed overload for a request that is not sealed
///     checks the runtime type, so a derived request never reaches the base request's handler (OQ-31).
/// </param>
/// <param name="ResponseType">The request's <c>TResponse</c> or the stream request's <c>TItem</c>; <see langword="null" /> for a notification.</param>
/// <param name="StaticInvoker">Whether <c>InvokeAsync</c> is static: the handler has no injected parameters and is never constructed.</param>
/// <param name="Services">The generated constructor's parameters (the handler's injected parameters), in order.</param>
/// <param name="Behaviors">The behaviors attached to the handler method, as <c>typeof</c> operands, in declared order (Phase 4).</param>
/// <param name="Order">The <c>[HandlerOrder]</c> of a notification handler (Phase 6).</param>
internal sealed record HostHandlerModel(
    string HandlerClass,
    HandlerKind Kind,
    string MessageType,
    bool MessageIsValueType,
    bool MessageIsSealed,
    string? ResponseType,
    bool StaticInvoker,
    EquatableArray<ServiceParameterModel> Services,
    EquatableArray<string> Behaviors,
    int Order
);

/// <summary>
///     One parameter of a generated constructor (a handler's, later a behavior's), resolved by a generated factory (ADR 0001, Injected
///     parameters).
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
/// <param name="IsServiceProvider">Whether the parameter is <c>IServiceProvider</c>, which receives the scope's provider itself.</param>
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
///     What the host's syntax pipeline found for one handler candidate.
/// </summary>
/// <param name="HandledType">
///     The concrete request or stream request type the candidate takes, fully qualified, even when the candidate breaks a rule: a malformed
///     handler is reported by the analyzers, and KRX030 does not add a second error for its request. <see langword="null" /> for a
///     notification handler or when the candidate has no single concrete request parameter.
/// </param>
/// <param name="Handler">The handler, or <see langword="null" /> when Keryx generates nothing for it in a host.</param>
internal sealed record HostHandlerEntry(string? HandledType, HostHandlerModel? Handler);

/// <summary>
///     A request or stream request type declared in the host, for KRX030.
/// </summary>
/// <param name="Type">The type, fully qualified with <c>global::</c>.</param>
/// <param name="DisplayName">The name the diagnostic shows.</param>
/// <param name="Location">The first declaration, where KRX030 is reported.</param>
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
///     The compilation references Keryx, has <c>[assembly: KeryxHost]</c> and compiles with C# 11 or later (KRX022). When false the host
///     generator emits and reports nothing.
/// </param>
/// <param name="Namespace">The namespace of the generated host types (ADR 0001, Opting in).</param>
/// <param name="AssemblyName">The host's assembly name, for messages.</param>
/// <param name="HostAttribute">Where <c>[assembly: KeryxHost]</c> is, for host diagnostics without a source location of their own.</param>
/// <param name="UnsupportedTargetFramework">
///     The target framework KRX031 reports when it is older than <c>net10.0</c>, or <see langword="null" /> when it is supported.
/// </param>
internal sealed record HostSettings(bool Enabled, string Namespace, string AssemblyName, LocationInfo? HostAttribute, string? UnsupportedTargetFramework)
{
    public static HostSettings Disabled { get; } = new(false, "", "", null, null);
}

/// <summary>
///     What the host generator reads from the compilation itself (fed directly by the compilation, so this step always runs).
/// </summary>
/// <param name="IsHost">The compilation references Keryx and has <c>[assembly: KeryxHost]</c>.</param>
/// <param name="AssemblyName">The assembly name.</param>
/// <param name="HostAttribute">Where <c>[assembly: KeryxHost]</c> is.</param>
/// <param name="RuntimeAssemblyName">The name of the referenced runtime assembly used when the target framework properties are absent.</param>
/// <param name="RuntimeAssemblyVersion">Its version, or <see langword="null" /> when no runtime assembly is referenced.</param>
internal sealed record HostCompilationInfo(
    bool IsHost,
    string AssemblyName,
    LocationInfo? HostAttribute,
    string? RuntimeAssemblyName,
    Version? RuntimeAssemblyVersion
);

/// <summary>
///     The MSBuild properties the host generator reads (<c>build/Keryx.props</c> makes them compiler visible).
/// </summary>
/// <param name="RootNamespace"><c>RootNamespace</c>, or <see langword="null" /> when absent or empty.</param>
/// <param name="TargetFrameworkIdentifier"><c>TargetFrameworkIdentifier</c>, or <see langword="null" /> when absent or empty.</param>
/// <param name="TargetFrameworkVersion"><c>TargetFrameworkVersion</c>, or <see langword="null" /> when absent or empty.</param>
internal sealed record HostBuildProperties(string? RootNamespace, string? TargetFrameworkIdentifier, string? TargetFrameworkVersion);
