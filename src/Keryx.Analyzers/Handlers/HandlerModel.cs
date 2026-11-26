using Specular.Scanning;

namespace Keryx.Analyzers.Handlers;

/// <summary>
///     One type declaration in the chain the generated partial repeats, outermost first; the last one is the handler class.
/// </summary>
/// <param name="Modifiers">The modifiers to repeat on the partial declaration (accessibility, <c>static</c>, <c>readonly</c>, <c>ref</c>).</param>
/// <param name="Keyword"><c>class</c>, <c>record</c>, <c>struct</c>, <c>record struct</c> or <c>interface</c>.</param>
/// <param name="Name">The type name, escaped when it is a keyword.</param>
internal sealed record TypeDeclarationModel(string Modifiers, string Keyword, string Name);

/// <summary>
///     One parameter of the handler method.
/// </summary>
/// <param name="Name">The parameter name, unescaped.</param>
/// <param name="Type">The fully qualified type, with nullable annotations.</param>
/// <param name="Role">Message, cancellation token or injected service.</param>
/// <param name="ServiceKey">The <c>[FromKeyedServices]</c> key as a C# expression, or <see langword="null" />.</param>
/// <param name="DefaultValue">The default value as a C# expression, or <see langword="null" /> when the parameter has none.</param>
internal sealed record ParameterModel(string Name, string Type, ParameterRole Role, string? ServiceKey, string? DefaultValue);

/// <summary>
///     Everything the generators need about one handler, value-equatable for incremental caching (no symbols, syntax or locations). The
///     handler generator emits the handler class's other half from it; the host generator composes pipelines from the same model.
/// </summary>
/// <param name="HintName">The generated file's hint name (ADR 0001, Incremental generation rules).</param>
/// <param name="Namespace">The handler class's namespace, or <see langword="null" /> for the global namespace.</param>
/// <param name="Types">The type declarations from the outermost containing type to the handler class.</param>
/// <param name="HandlerClass">The handler class, fully qualified.</param>
/// <param name="Kind">What the handler handles.</param>
/// <param name="MessageType">The request, stream request or notification parameter type, fully qualified.</param>
/// <param name="ResponseType">The request's <c>TResponse</c> or the stream request's <c>TItem</c>; <see langword="null" /> for a notification.</param>
/// <param name="ReturnShape">How the handler's result is adapted.</param>
/// <param name="NullabilityDiffers">Whether the handler's result differs from <paramref name="ResponseType" /> in nullability only (KRX017).</param>
/// <param name="MethodName"><c>Handle</c> or <c>HandleAsync</c>.</param>
/// <param name="Parameters">The handler method's parameters, in order.</param>
/// <param name="Behaviors">The behaviors attached to the handler method, as <c>typeof</c> operands, in declared order.</param>
/// <param name="Order">The <c>[HandlerOrder]</c> of a notification handler.</param>
/// <param name="ValidOutsideHost">Whether the rules that apply only outside a host (public chain, KRX016) hold.</param>
/// <param name="ClassAccess">How widely the handler class can be used.</param>
/// <param name="SignatureAccess">How widely the message, response and injected types can be used, at the narrowest.</param>
internal sealed record HandlerModel(
    string HintName,
    string? Namespace,
    EquatableArray<TypeDeclarationModel> Types,
    string HandlerClass,
    HandlerKind Kind,
    string MessageType,
    string? ResponseType,
    ReturnShape ReturnShape,
    bool NullabilityDiffers,
    string MethodName,
    EquatableArray<ParameterModel> Parameters,
    EquatableArray<string> Behaviors,
    int Order,
    bool ValidOutsideHost,
    AccessLevel ClassAccess,
    AccessLevel SignatureAccess
)
{
    /// <summary>Whether the handler has no injected parameters, so its invoker is static and it has no generated constructor.</summary>
    public bool StaticInvoker => Parameters.All(static p => p.Role != ParameterRole.Injected);
}

/// <summary>
///     The compilation-wide inputs of the handler generator.
/// </summary>
/// <param name="Enabled">Keryx is referenced and the language version is C# 14 or later (KRX022).</param>
/// <param name="IsHost">The project is a host (<c>build_property.KeryxHost</c> is <c>true</c>).</param>
internal sealed record HandlerGeneratorSettings(bool Enabled, bool IsHost);
