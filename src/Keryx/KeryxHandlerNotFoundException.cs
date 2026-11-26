namespace Keryx;

/// <summary>
///     Thrown synchronously, before any behavior runs, when the mediator knows no handler for the runtime type of a request or stream request it
///     was asked to dispatch.
/// </summary>
/// <param name="requestType">The runtime type that has no handler.</param>
[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "The exception always names the request type (ADR 0001, Core types)."
)]
public sealed class KeryxHandlerNotFoundException(Type requestType) : InvalidOperationException(CreateMessage(requestType))
{
    private static string CreateMessage(Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        return "No Keryx handler is known for the request type '"
         + ( requestType.FullName ?? requestType.Name )
         + "'. Reference the assembly that contains its handler (and the Keryx package) from the host project, the one marked with "
         + "[assembly: KeryxHost].";
    }

    /// <summary>
    ///     The runtime type that has no handler.
    /// </summary>
    public Type RequestType { get; } = requestType;
}
