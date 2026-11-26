using System.Runtime.CompilerServices;
using Keryx.Samples.Contracts;

namespace Keryx.Samples.Orders;

public partial class PlaceOrderHandler
{
    [Behavior(typeof(ValidateQuantity))]
    private static async ValueTask<int> Handle(PlaceOrder request, IOrderStore store, IPublisher publisher, CancellationToken cancellationToken)
    {
        var number = store.Add(request.Sku, request.Quantity);
        await publisher.Publish(new OrderPlaced(number, request.Sku), cancellationToken).ConfigureAwait(false);
        return number;
    }
}

public partial class GetOrderHandler
{
    private static OrderSummary? Handle(GetOrder request, IOrderStore store) => store.Find(request.Number);
}

public partial class CancelOrderHandler
{
    private static ValueTask Handle(CancelOrder request, IOrderStore store, IPublisher publisher, CancellationToken cancellationToken) =>
        store.Remove(request.Number) ? publisher.Publish(new OrderCancelled(request.Number), cancellationToken) : default;
}

public partial class ListOrdersHandler
{
    private static async IAsyncEnumerable<OrderSummary> Handle(
        ListOrders request,
        IOrderStore store,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        foreach (var order in store.All.ToArray())
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return order;
        }
    }
}

/// <summary>Runs after the other handlers of <see cref="OrderPlaced" /> (<c>[HandlerOrder(1)]</c>).</summary>
public partial class RecordPlacedOrder
{
    [HandlerOrder(1)]
    private static ValueTask Handle(OrderPlaced notification, IActivityLog log)
    {
        log.Write($"placed #{notification.Number} ({notification.Sku})");
        return default;
    }
}

/// <summary>A polymorphic handler: receives every concrete <see cref="IOrderEvent" /> a host knows, including the host's own.</summary>
public partial class AuditOrderEvents
{
    private static ValueTask Handle(IOrderEvent notification, IActivityLog log)
    {
        log.Write($"audit {notification.GetType().Name} #{notification.Number}");
        return default;
    }
}
