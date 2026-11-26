namespace Keryx;

/// <summary>
///     Makes this assembly a host: it gets a generated, internal, scoped <c>KeryxMediator</c> (an <see cref="IMediator" />) and an internal
///     <c>AddKeryx()</c> that registers it, and it composes every pipeline from its own and its references' handlers and behaviors.
/// </summary>
/// <remarks>A host must target <c>net10.0</c> or newer (KRX031).</remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class KeryxHostAttribute : Attribute;

/// <summary>
///     Marks this assembly as a contracts assembly: it declares requests or notifications whose handlers live elsewhere, so KRX001 (request
///     without a handler) is not reported here.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class KeryxContractsAttribute : Attribute;

/// <summary>
///     In a host, marks a request or stream request type as handled outside this host (for example out of process), so the host does not
///     report KRX030 for it. Name each such type with its own attribute: <c>[assembly: ExternallyHandled(typeof(X))]</c>.
/// </summary>
/// <remarks>
///     A type the host handles, or a type that is not a request or stream request, is KRX036. Outside a host the attribute has no effect
///     and is KRX043.
/// </remarks>
/// <param name="requestType">The request or stream request type handled outside this host.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ExternallyHandledAttribute(Type requestType) : Attribute
{
    /// <summary>
    ///     The request or stream request type handled outside this host.
    /// </summary>
    public Type RequestType { get; } = requestType;
}
