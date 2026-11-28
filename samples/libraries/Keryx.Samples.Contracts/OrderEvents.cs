using Keryx;

namespace Keryx.Samples.Contracts;

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
