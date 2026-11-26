using System.ComponentModel;

namespace Keryx.Internal;

/// <summary>
///     Manifest entry the generator puts on every generated public handler partial, so a host can find the handler in compiled metadata.
///     Used by generated code only.
/// </summary>
/// <param name="targetType">The request, stream request or notification parameter type of the handler.</param>
/// <param name="kind">What the handler handles.</param>
[AttributeUsage(AttributeTargets.Class), EditorBrowsable(EditorBrowsableState.Never)]
public sealed class KeryxHandlerAttribute(Type targetType, HandlerKind kind) : Attribute
{
    /// <summary>
    ///     The request, stream request or notification parameter type of the handler. For a polymorphic notification handler, a base type or
    ///     interface.
    /// </summary>
    public Type TargetType { get; } = targetType;

    /// <summary>
    ///     What the handler handles.
    /// </summary>
    public HandlerKind Kind { get; } = kind;

    /// <summary>
    ///     The behavior classes attached to a request or stream request handler method, in declared order. Empty for a notification handler,
    ///     which has no behaviors of its own.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Attribute arguments are arrays.")]
    public Type[] Behaviors { get; init; } = [];

    /// <summary>
    ///     The <see cref="HandlerOrderAttribute.Order" /> of a notification handler.
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    ///     <see langword="true" /> when the handler has no injected parameters, so the generated <c>InvokeAsync</c> is static.
    /// </summary>
    public bool StaticInvoker { get; init; }
}

/// <summary>
///     Marks an assembly whose handler partials the Keryx generator produced, and the manifest format it used. Used by generated code only.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly), EditorBrowsable(EditorBrowsableState.Never)]
public sealed class KeryxManifestAttribute : Attribute
{
    /// <summary>
    ///     The manifest format version. A host reports KRX032 for a version newer than it understands.
    /// </summary>
    public int FormatVersion { get; init; }
}
