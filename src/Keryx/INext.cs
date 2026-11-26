namespace Keryx;

/// <summary>
///     The rest of a request pipeline, as seen by an <see cref="IPipelineBehavior{TRequest,TResponse}" />: the next behavior, or the
///     handler.
/// </summary>
/// <remarks>
///     Implemented by generated link structs. A behavior may call <see cref="InvokeAsync" /> more than once (for example to retry), or not at
///     all (for example on a cache hit).
/// </remarks>
/// <typeparam name="TResponse">The response type of the pipeline.</typeparam>
public interface INext<TResponse>
{
    /// <summary>
    ///     Runs the rest of the pipeline.
    /// </summary>
    /// <param name="cancellationToken">The token to pass on. Usually the one the behavior received.</param>
    /// <returns>The response produced by the rest of the pipeline.</returns>
    ValueTask<TResponse> InvokeAsync(CancellationToken cancellationToken);
}

/// <summary>
///     The rest of a notification pipeline, as seen by an <see cref="INotificationBehavior{TNotification}" /> or an
///     <see cref="INotificationHandlerBehavior{TNotification}" />: the next behavior, or the handlers.
/// </summary>
/// <remarks>
///     Implemented by generated link structs. A behavior may call <see cref="InvokeAsync" /> more than once, or not at all.
/// </remarks>
public interface INext
{
    /// <summary>
    ///     Runs the rest of the pipeline.
    /// </summary>
    /// <param name="cancellationToken">The token to pass on. Usually the one the behavior received.</param>
    /// <returns>A task that completes when the rest of the pipeline has run.</returns>
    ValueTask InvokeAsync(CancellationToken cancellationToken);
}
