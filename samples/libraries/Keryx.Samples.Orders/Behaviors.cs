using Keryx.Samples.Contracts;

namespace Keryx.Samples.Orders;

/// <summary>
///     Logs every request, around everything else: a global behavior, so every host that references this library applies it to every
///     request it composes (a host removes it with <c>[assembly: ExcludeBehavior(typeof(Logging))]</c>). The behavior method is open, and
///     its activity log is injected once per scope into the generated pipeline context.
/// </summary>
[RequestBehavior]
[GlobalBehavior]
[BehaviorOrder(Phase.Observability)]
public static class Logging
{
    public static async ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(
        TRequest request,
        TNext next,
        IActivityLog log,
        CancellationToken cancellationToken
    )
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        log.Write("-> " + typeof(TRequest).Name);
        var response = await next.InvokeAsync(cancellationToken).ConfigureAwait(false);
        log.Write("<- " + typeof(TRequest).Name);
        return response;
    }
}

/// <summary>
///     Rejects an order without a positive quantity: a closed behavior method, attached to the <see cref="PlaceOrder" /> handler only.
/// </summary>
[RequestBehavior]
[BehaviorOrder(Phase.Validation)]
public static class ValidateQuantity
{
    public static ValueTask<int> Handle<TNext>(PlaceOrder request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext<int> =>
        request.Quantity > 0
            ? next.InvokeAsync(cancellationToken)
            : throw new ArgumentOutOfRangeException(nameof(request), request.Quantity, "An order needs a positive quantity.");
}
