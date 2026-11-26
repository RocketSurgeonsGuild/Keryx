namespace Keryx;

/// <summary>
///     A stream behavior that observes a stream request through hooks instead of wrapping it. Hook behaviors that sit next to each other in
///     a pipeline (with no <see cref="IStreamPipelineBehavior{TRequest,TItem}" /> between them) are fused into one generated async iterator,
///     so they cost one iterator per fused segment rather than one per behavior.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item><see cref="OnStartAsync" /> runs outer to inner, lazily on the first <c>MoveNextAsync</c>.</item>
///         <item>Items travel outward from the handler: <see cref="OnItem" /> runs inner to outer.</item>
///         <item><see cref="OnCompleteAsync" /> runs inner to outer on normal completion and on early dispose.</item>
///         <item><see cref="OnErrorAsync" /> runs inner to outer for any exception, which is then rethrown.</item>
///         <item>
///             <see cref="OnCompleteAsync" /> and <see cref="OnErrorAsync" /> run only for behaviors whose <see cref="OnStartAsync" />
///             completed.
///         </item>
///     </list>
///     Implement the hooks implicitly as public, non-virtual methods; generated code calls them on the concrete, closed behavior type.
/// </remarks>
/// <typeparam name="TRequest">The stream request type.</typeparam>
/// <typeparam name="TItem">The item type.</typeparam>
public interface IStreamHookBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    /// <summary>
    ///     Runs before the first item is requested from the rest of the pipeline.
    /// </summary>
    /// <param name="request">The stream request.</param>
    /// <param name="cancellationToken">The token of the enumeration.</param>
    /// <returns>A task that completes when the hook is done.</returns>
    ValueTask OnStartAsync(TRequest request, CancellationToken cancellationToken);

    /// <summary>
    ///     Runs for each item on its way out to the consumer. The item may be replaced through <paramref name="item" />.
    /// </summary>
    /// <param name="request">The stream request.</param>
    /// <param name="item">The item. Assign to replace it.</param>
    /// <returns>
    ///     <see langword="true" /> to pass the item on; <see langword="false" /> to drop it, so that outer behaviors and the consumer never
    ///     see it.
    /// </returns>
    [SuppressMessage("Design", "CA1045:Do not pass types by reference", Justification = "Lets a hook replace a (struct) item without copying (ADR 0001).")]
    bool OnItem(TRequest request, ref TItem item);

    /// <summary>
    ///     Runs when the stream completes normally or the consumer disposes it early.
    /// </summary>
    /// <param name="request">The stream request.</param>
    /// <returns>A task that completes when the hook is done.</returns>
    ValueTask OnCompleteAsync(TRequest request);

    /// <summary>
    ///     Observes an exception from the rest of the pipeline or from a hook, including <see cref="OperationCanceledException" />. The
    ///     exception is rethrown after every hook has seen it.
    /// </summary>
    /// <param name="request">The stream request.</param>
    /// <param name="exception">The exception.</param>
    /// <returns>A task that completes when the hook is done.</returns>
    ValueTask OnErrorAsync(TRequest request, Exception exception);
}
