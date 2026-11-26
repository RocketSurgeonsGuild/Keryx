namespace Keryx;

/// <summary>
///     A behavior that wraps the handler of a stream request: it receives the request, gets the inner stream from
///     <see cref="IStreamNext{TItem}.InvokeAsync" /> and returns the stream the outer behavior or the caller enumerates.
/// </summary>
/// <remarks>
///     <para>
///         Usually written as an <see langword="async" /> iterator that re-yields the inner stream with <see langword="await" />
///         <see langword="foreach" />. Put <c>[EnumeratorCancellation]</c> on <c>cancellationToken</c> and pass that token on to
///         <see cref="IStreamNext{TItem}.InvokeAsync" />, so the consumer's <c>WithCancellation</c> token reaches the rest of the pipeline.
///     </para>
///     <para>
///         Implement <see cref="Handle{TNext}" /> implicitly as a public, non-virtual method. Generated code calls it on the concrete, closed
///         behavior type, never through this interface, so there is no generic virtual dispatch. A behavior class that is
///         <see langword="abstract" />, or whose <c>Handle</c> is <see langword="virtual" />, <see langword="abstract" /> or
///         <see langword="override" />, is KRX019.
///     </para>
///     <para>
///         <see cref="IStreamHookBehavior{TRequest,TItem}" /> is the hook form: hook behaviors between two wrap behaviors are fused into one
///         generated iterator.
///     </para>
/// </remarks>
/// <typeparam name="TRequest">The stream request type.</typeparam>
/// <typeparam name="TItem">The item type.</typeparam>
public interface IStreamPipelineBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    /// <summary>
    ///     Wraps the rest of the stream pipeline.
    /// </summary>
    /// <param name="request">The stream request.</param>
    /// <param name="next">
    ///     The rest of the pipeline. Call <see cref="IStreamNext{TItem}.InvokeAsync" /> to get the inner stream.
    /// </param>
    /// <param name="cancellationToken">The token of the <c>Stream</c> call, or of the enumeration when the behavior is an iterator.</param>
    /// <typeparam name="TNext">The generated link struct for the rest of the pipeline.</typeparam>
    /// <returns>The stream to return to the outer behavior or to the caller.</returns>
    IAsyncEnumerable<TItem> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IStreamNext<TItem>;
}
