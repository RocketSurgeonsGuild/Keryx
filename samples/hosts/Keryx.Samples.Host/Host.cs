using Keryx;
using Keryx.Samples.Contracts;

[assembly: KeryxHost]

namespace Keryx.Samples.Host;

/// <summary>A request of the host itself, handled next to it.</summary>
public sealed record Ping(string Message) : IRequest<string>;

public static partial class PingHandler
{
}

/// <summary>A notification type of the host: the library's polymorphic <c>AuditOrderEvents</c> receives it too.</summary>
public sealed record OrderShipped(int Number) : IOrderEvent;

/// <summary>The host's own handler of a library notification type.</summary>
public partial class PrintPlacedOrder
{
}
