using System.ComponentModel;

namespace Keryx.Internal;

/// <summary>
///     What a generated handler class handles. Used by generated code only.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public enum HandlerKind
{
    /// <summary>
    ///     A request (<see cref="IRequest{TResponse}" />).
    /// </summary>
    Request,

    /// <summary>
    ///     A stream request (<see cref="IStreamRequest{TItem}" />).
    /// </summary>
    Stream,

    /// <summary>
    ///     A notification (<see cref="INotification" />).
    /// </summary>
    Notification,
}
