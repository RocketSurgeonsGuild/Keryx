namespace Keryx;

/// <summary>
///     A behavior that wraps one whole <see cref="IPublisher.Publish{TNotification}" /> call: every handler and the publish strategy.
/// </summary>
/// <remarks>
///     Attached on the notification type, per assembly (the notification type's assembly) or globally. For a notification type the host
///     knows, it runs even when there are no handlers. Implement <see cref="Handle{TNext}" /> implicitly as a public, non-virtual method.
/// </remarks>
/// <typeparam name="TNotification">The concrete published notification type.</typeparam>
public interface INotificationPipelineBehavior<TNotification>
    where TNotification : INotification
{
    /// <summary>
    ///     Runs this behavior around the rest of the publish pipeline.
    /// </summary>
    /// <param name="notification">The notification being published.</param>
    /// <param name="next">The rest of the pipeline. Call <see cref="INext.InvokeAsync" /> to continue.</param>
    /// <param name="cancellationToken">The token of the <c>Publish</c> call.</param>
    /// <typeparam name="TNext">The generated link struct for the rest of the pipeline.</typeparam>
    /// <returns>A task that completes when this behavior is done.</returns>
    ValueTask Handle<TNext>(TNotification notification, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext;
}

/// <summary>
///     A behavior that wraps each notification handler call separately.
/// </summary>
/// <remarks>
///     Attached on the handler method, per assembly (the handler's assembly) or globally. <typeparamref name="TNotification" /> is always the
///     concrete published type, also for a polymorphic handler. Implement <see cref="Handle{TNext}" /> implicitly as a public, non-virtual
///     method.
/// </remarks>
/// <typeparam name="TNotification">The concrete published notification type.</typeparam>
public interface INotificationHandlerPipelineBehavior<TNotification>
    where TNotification : INotification
{
    /// <summary>
    ///     Runs this behavior around one handler call.
    /// </summary>
    /// <param name="notification">The notification being published.</param>
    /// <param name="next">The rest of this handler's pipeline. Call <see cref="INext.InvokeAsync" /> to continue.</param>
    /// <param name="cancellationToken">The token of the <c>Publish</c> call.</param>
    /// <typeparam name="TNext">The generated link struct for the rest of the pipeline.</typeparam>
    /// <returns>A task that completes when this behavior is done.</returns>
    ValueTask Handle<TNext>(TNotification notification, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext;
}
