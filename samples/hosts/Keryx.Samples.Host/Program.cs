using Keryx;
using Keryx.Samples.Contracts;
using Keryx.Samples.Host;
using Keryx.Samples.Orders;
using Microsoft.Extensions.DependencyInjection;

var log = new ActivityLog();
var services = new ServiceCollection();
services.AddSingleton<IOrderStore>(new InMemoryOrderStore());
services.AddSingleton<IActivityLog>(log);
services.AddKeryx();

await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
await using var scope = provider.CreateAsyncScope();
var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
// The host's own mediator type has typed overloads, which skip type dispatch.
var typed = scope.ServiceProvider.GetRequiredService<KeryxMediator>();

var failures = new List<string>();

var apples = await mediator.Send(new PlaceOrder("apple", 3));
var pears = await typed.Send(new PlaceOrder("pear", 1));
Check(pears == apples + 1, $"order numbers {apples} and {pears}");

var found = await mediator.Send(new GetOrder(apples));
Check(found is { Sku: "apple", Quantity: 3 }, $"GetOrder returned {found}");
Check(await typed.Send(new GetOrder(-1)) is null, "GetOrder of an unknown number");

await typed.Send(new CancelOrder(pears));
var open = new List<OrderSummary>();
await foreach (var order in mediator.Stream(new ListOrders()))
{
    open.Add(order);
}

Check(open.Count == 1 && open[0].Number == apples, $"open orders: {string.Join(", ", open.Select(static order => order.ToString()))}");

await mediator.Publish(new OrderShipped(apples));
Check(await typed.Send(new Ping("hello")) == "pong: hello", "Ping");

try
{
    _ = await mediator.Send(new PlaceOrder("nothing", 0));
    failures.Add("an order of 0 items was accepted");
}
catch (ArgumentOutOfRangeException)
{
    // ValidateQuantity, attached to the PlaceOrder handler in the library.
}

string[] expected =
[
    "-> PlaceOrder",
    $"host saw #{apples}",
    $"audit OrderPlaced #{apples}",
    $"placed #{apples} (apple)",
    "<- PlaceOrder",
    "-> PlaceOrder",
    $"host saw #{pears}",
    $"audit OrderPlaced #{pears}",
    $"placed #{pears} (pear)",
    "<- PlaceOrder",
    "-> GetOrder",
    "<- GetOrder",
    "-> GetOrder",
    "<- GetOrder",
    "-> CancelOrder",
    $"audit OrderCancelled #{pears}",
    "<- CancelOrder",
    $"audit OrderShipped #{apples}",
    "-> Ping",
    "<- Ping",
    "-> PlaceOrder",
];
Check(log.Entries.SequenceEqual(expected), "activity log:" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", log.Entries));

foreach (var entry in log.Entries)
{
    Console.WriteLine(entry);
}

if (failures.Count > 0)
{
    foreach (var failure in failures)
    {
        Console.Error.WriteLine("FAILED: " + failure);
    }

    return 1;
}

Console.WriteLine("Keryx sample host: every check passed.");
return 0;

void Check(bool condition, string what)
{
    if (!condition)
    {
        failures.Add(what);
    }
}
