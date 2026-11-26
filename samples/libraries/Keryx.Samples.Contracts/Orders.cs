using Keryx;

[assembly: KeryxContracts]

namespace Keryx.Samples.Contracts;

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

/// <summary>Something that happened to an order. Handlers typed with this interface receive every order event (polymorphic handlers).</summary>
public interface IOrderEvent : INotification
{
    int Number { get; }
}

/// <summary>Published when an order is placed.</summary>
public sealed class OrderPlaced(int number, string sku) : IOrderEvent
{
    public int Number { get; } = number;

    public string Sku { get; } = sku;
}

/// <summary>Published when an order is cancelled.</summary>
public sealed class OrderCancelled(int number) : IOrderEvent
{
    public int Number { get; } = number;
}
