using Microsoft.Extensions.Logging;

namespace Keryx.Benchmarks.Common;

/// <summary>
///     The messages the real-world scenarios write, as source-generated logger methods: the two every logging behavior (and the baseline)
///     writes per request or per publish, and the one every real-world notification handler writes.
/// </summary>
public static partial class RequestLog
{
    /// <summary>Before the handler.</summary>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Handling {RequestName}")]
    public static partial void Handling(ILogger logger, string requestName);

    /// <summary>After the handler, with the response (formatted with its <c>ToString</c>).</summary>
    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Handled {RequestName}: {Response}")]
    public static partial void Handled(ILogger logger, string requestName, object? response);

    /// <summary>Before a notification's handlers (the notification logging behavior).</summary>
    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Publishing {NotificationName}")]
    public static partial void Publishing(ILogger logger, string notificationName);

    /// <summary>After a notification's handlers (the notification logging behavior).</summary>
    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Published {NotificationName}")]
    public static partial void Published(ILogger logger, string notificationName);

    /// <summary>What every real-world notification handler writes: the pong it found.</summary>
    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Pinged {Id}: {Name}")]
    public static partial void Pinged(ILogger logger, int id, string name);
}
