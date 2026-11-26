namespace Keryx;

/// <summary>
///     A behavior that wraps the handler of a request.
/// </summary>
/// <remarks>
///     <para>
///         Implement <see cref="Handle{TNext}" /> implicitly as a public, non-virtual method. Generated code calls it on the concrete,
///         closed behavior type, never through this interface, so there is no generic virtual dispatch. A behavior class that is
///         <see langword="abstract" />, or whose <c>Handle</c> is <see langword="virtual" />, <see langword="abstract" /> or
///         <see langword="override" />, is KRX019.
///     </para>
///     <para>
///         A behavior class is either open with the same type parameters (<c>AuditBehavior&lt;TRequest, TResponse&gt;</c>) or closed and
///         non-generic (<c>AuditCreate : IRequestPipelineBehavior&lt;CreateOrder.Command, Guid&gt;</c>), and has exactly one public
///         constructor.
///     </para>
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type. <see cref="Unit" /> for an <see cref="IRequest" />.</typeparam>
public interface IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    ///     Runs this behavior around the rest of the pipeline.
    /// </summary>
    /// <param name="request">The request being sent.</param>
    /// <param name="next">The rest of the pipeline. Call <see cref="INext{TResponse}.InvokeAsync" /> to continue.</param>
    /// <param name="cancellationToken">The token of the <c>Send</c> call.</param>
    /// <typeparam name="TNext">The generated link struct for the rest of the pipeline.</typeparam>
    /// <returns>The response to return to the outer behavior or to the caller.</returns>
    ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext<TResponse>;
}
