using Keryx.Samples.Contracts;

namespace Keryx.Samples.Orders;

/// <summary>
///     Logs every request, around everything else: a global behavior, so every host that references this library applies it to every
///     request it composes (a host removes it with <c>[assembly: ExcludeBehavior(typeof(LoggingBehavior&lt;,&gt;))]</c>).
/// </summary>
[GlobalBehavior]
[BehaviorOrder(Phase.Observability)]
public sealed class LoggingBehavior<TRequest, TResponse>(IActivityLog log) : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext<TResponse>
    {
        log.Write("-> " + typeof(TRequest).Name);
        var response = await next.InvokeAsync(cancellationToken).ConfigureAwait(false);
        log.Write("<- " + typeof(TRequest).Name);
        return response;
    }
}

/// <summary>Rejects an order without a positive quantity: a closed behavior, attached to the <see cref="PlaceOrder" /> handler only.</summary>
[BehaviorOrder(Phase.Validation)]
public sealed class ValidateQuantity : IRequestPipelineBehavior<PlaceOrder, int>
{
    public ValueTask<int> Handle<TNext>(PlaceOrder request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext<int> =>
        request.Quantity > 0
            ? next.InvokeAsync(cancellationToken)
            : throw new ArgumentOutOfRangeException(nameof(request), request.Quantity, "An order needs a positive quantity.");
}
