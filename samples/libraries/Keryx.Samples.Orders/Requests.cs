namespace Keryx.Samples.Orders;

// A request's handler lives in the request's own assembly (KRX023), so the order requests are declared here, next to their handlers
// (Handlers.cs). The order notifications stay in Keryx.Samples.Contracts: a notification may be handled in any assembly.

/// <summary>Places an order; the response is the new order's number.</summary>
public sealed class PlaceOrder(string sku, int quantity) : IRequest<int>
{
    public string Sku { get; } = sku;

    public int Quantity { get; } = quantity;
}

/// <summary>Looks an order up by number; the response is <see langword="null" /> for an unknown number.</summary>
public sealed class GetOrder(int number) : IRequest<OrderSummary?>
{
    public int Number { get; } = number;
}

/// <summary>Cancels an order. A request without a response: its handler returns nothing.</summary>
public sealed class CancelOrder(int number) : IRequest
{
    public int Number { get; } = number;
}

/// <summary>Streams every open order, oldest first.</summary>
public sealed class ListOrders : IStreamRequest<OrderSummary>;

/// <summary>One order.</summary>
public sealed class OrderSummary(int number, string sku, int quantity)
{
    public int Number { get; } = number;

    public string Sku { get; } = sku;

    public int Quantity { get; } = quantity;

    public override string ToString() => $"#{Number} {Quantity} x {Sku}";
}
