namespace Keryx;

/// <summary>
///     The rest of a stream pipeline, as seen by an <see cref="IStreamPipelineBehavior{TRequest,TItem}" />: the next behavior, a fused hook
///     segment, or the handler.
/// </summary>
/// <remarks>
///     Implemented by generated link structs. A behavior may call <see cref="InvokeAsync" /> more than once (for example to restart the
///     stream), or not at all (for example to serve cached items).
/// </remarks>
/// <typeparam name="TItem">The item type of the pipeline.</typeparam>
public interface IStreamNext<TItem>
{
    /// <summary>
    ///     Gets the stream produced by the rest of the pipeline.
    /// </summary>
    /// <param name="cancellationToken">The token to pass on. Usually the one the behavior received.</param>
    /// <returns>The stream produced by the rest of the pipeline.</returns>
    IAsyncEnumerable<TItem> InvokeAsync(CancellationToken cancellationToken);
}
