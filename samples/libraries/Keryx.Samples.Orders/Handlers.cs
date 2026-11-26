using System.Runtime.CompilerServices;
using Keryx.Samples.Contracts;

namespace Keryx.Samples.Orders;

public partial class PlaceOrderHandler
{
}

public partial class GetOrderHandler
{
}

public partial class CancelOrderHandler
{
}

public partial class ListOrdersHandler
{
}

/// <summary>Runs after the other handlers of <see cref="OrderPlaced" /> (<c>[HandlerOrder(1)]</c>).</summary>
public partial class RecordPlacedOrder
{
}

/// <summary>A polymorphic handler: receives every concrete <see cref="IOrderEvent" /> a host knows, including the host's own.</summary>
public partial class AuditOrderEvents
{
}
