namespace Keryx;

/// <summary>
///     Sends requests and stream requests to their handlers.
/// </summary>
public interface ISender
{
    /// <summary>
    ///     Sends a request through its pipeline to its handler and returns the response.
    /// </summary>
    /// <param name="request">The request. Dispatch is by its exact runtime type.</param>
    /// <param name="cancellationToken">The token passed to every behavior and to the handler.</param>
    /// <typeparam name="TResponse">The response type declared by the request.</typeparam>
    /// <returns>The handler's response, after every behavior has run.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="KeryxHandlerNotFoundException">The host knows no handler for the request's runtime type.</exception>
    ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Sends a stream request through its pipeline to its handler and returns the stream of items.
    /// </summary>
    /// <param name="request">The stream request. Dispatch is by its exact runtime type.</param>
    /// <param name="cancellationToken">The token passed to every stream behavior and to the handler.</param>
    /// <typeparam name="TItem">The item type declared by the stream request.</typeparam>
    /// <returns>The items produced by the handler, after every stream behavior has seen them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="KeryxHandlerNotFoundException">The host knows no handler for the stream request's runtime type.</exception>
    IAsyncEnumerable<TItem> Stream<TItem>(IStreamRequest<TItem> request, CancellationToken cancellationToken = default);
}

/// <summary>
///     Publishes notifications to their handlers.
/// </summary>
public interface IPublisher
{
    /// <summary>
    ///     Publishes a notification to every handler that accepts its runtime type, including polymorphic handlers.
    /// </summary>
    /// <remarks>
    ///     Every handler runs, whatever the others do. When any handler fails, the returned task faults with one
    ///     <see cref="AggregateException" /> containing every failure in handler order.
    /// </remarks>
    /// <param name="notification">The notification. Dispatch is by its runtime type.</param>
    /// <param name="cancellationToken">The token passed to every behavior and handler. Cancelling it does not skip handlers.</param>
    /// <typeparam name="TNotification">
    ///     The static type of the notification. With a value type, dispatch never boxes the notification.
    /// </typeparam>
    /// <returns>A task that completes when every handler has run.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="notification" /> is <see langword="null" />.</exception>
    ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}

/// <summary>
///     Sends requests and stream requests (<see cref="ISender" />) and publishes notifications (<see cref="IPublisher" />).
/// </summary>
/// <remarks>
///     A host (a project whose <c>KeryxHost</c> MSBuild property is <c>true</c>) gets a generated, scoped implementation and registers it with its generated <c>AddKeryx()</c>.
/// </remarks>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Combines ISender and IPublisher.")]
public interface IMediator : ISender, IPublisher;
