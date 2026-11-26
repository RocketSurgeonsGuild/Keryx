using Keryx.Samples.Contracts;

namespace Keryx.Samples.Orders;

/// <summary>Where orders are kept.</summary>
public interface IOrderStore
{
    int Add(string sku, int quantity);

    OrderSummary? Find(int number);

    bool Remove(int number);

    IReadOnlyList<OrderSummary> All { get; }
}

/// <summary>An <see cref="IOrderStore" /> in memory.</summary>
public sealed class InMemoryOrderStore : IOrderStore
{
    private readonly List<OrderSummary> _orders = [];
    private int _next = 1000;

    public IReadOnlyList<OrderSummary> All => _orders;

    public int Add(string sku, int quantity)
    {
        var number = ++_next;
        _orders.Add(new(number, sku, quantity));
        return number;
    }

    public OrderSummary? Find(int number) => _orders.Find(order => order.Number == number);

    public bool Remove(int number) => _orders.RemoveAll(order => order.Number == number) > 0;
}

/// <summary>What the pipeline and the handlers did, in order.</summary>
public interface IActivityLog
{
    IReadOnlyList<string> Entries { get; }

    void Write(string entry);
}

/// <summary>An <see cref="IActivityLog" /> in memory.</summary>
public sealed class ActivityLog : IActivityLog
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries => _entries;

    public void Write(string entry) => _entries.Add(entry);
}
