namespace Keryx;

/// <summary>
///     Makes this assembly a host: it gets a generated, internal, scoped <c>KeryxMediator</c> (an <see cref="IMediator" />) and an internal
///     <c>AddKeryx()</c> that registers it, and it composes every pipeline from its own and its references' handlers and behaviors.
/// </summary>
/// <remarks>A host must target <c>net10.0</c> or newer (KRX031).</remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class KeryxHostAttribute : Attribute;
