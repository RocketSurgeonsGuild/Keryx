namespace Keryx;

/// <summary>
///     A behavior that observes a stream request through hooks. All hooks of one pipeline are fused into one generated async iterator.
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
public interface IStreamPipelineBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    /// <summary>
    ///     Runs before the first item is requested from the handler.
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
    ///     Observes an exception from the handler or from a hook, including <see cref="OperationCanceledException" />. The exception is
    ///     rethrown after every hook has seen it.
    /// </summary>
    /// <param name="request">The stream request.</param>
    /// <param name="exception">The exception.</param>
    /// <returns>A task that completes when the hook is done.</returns>
    ValueTask OnErrorAsync(TRequest request, Exception exception);
}

/// <summary>
///     A behavior that receives and returns the whole stream of a stream request.
/// </summary>
/// <remarks>
///     A wrap behavior splits hook fusion at its position: <see cref="IStreamPipelineBehavior{TRequest,TItem}" /> hooks inside it are fused
///     into one iterator, and hooks outside it into another.
/// </remarks>
/// <typeparam name="TRequest">The stream request type.</typeparam>
/// <typeparam name="TItem">The item type.</typeparam>
public interface IStreamWrapPipelineBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    /// <summary>
    ///     Wraps the rest of the stream pipeline.
    /// </summary>
    /// <param name="request">The stream request.</param>
    /// <param name="next">The stream produced by the rest of the pipeline.</param>
    /// <param name="cancellationToken">The token of the <c>Stream</c> call.</param>
    /// <returns>The stream to return to the outer behavior or to the caller.</returns>
    IAsyncEnumerable<TItem> Handle(TRequest request, IAsyncEnumerable<TItem> next, CancellationToken cancellationToken);
}
