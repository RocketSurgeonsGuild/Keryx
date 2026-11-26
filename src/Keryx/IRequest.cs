namespace Keryx;

/// <summary>
///     Marks a request: a message sent with <see cref="ISender.Send{TResponse}" /> that has exactly one handler, which produces a
///     <typeparamref name="TResponse" />.
/// </summary>
/// <remarks>
///     <para>
///         A request is a non-abstract, non-generic class, record or struct that implements exactly one closing of
///         <see cref="IRequest{TResponse}" /> and no closing of <see cref="IStreamRequest{TItem}" />. Dispatch is by exact runtime type.
///     </para>
///     <para>
///         The interface is invariant on purpose: generated dispatch reinterprets the handler's <c>ValueTask&lt;TExact&gt;</c> as
///         <c>ValueTask&lt;TResponse&gt;</c>, which is only safe when <typeparamref name="TResponse" /> is exactly the declared type.
///     </para>
/// </remarks>
/// <typeparam name="TResponse">The type of the response.</typeparam>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker interface: generated code dispatches on it.")]
public interface IRequest<TResponse>;

/// <summary>
///     Marks a request with no response. It is <see cref="IRequest{TResponse}" /> of <see cref="Unit" />, so every send path returns
///     <c>ValueTask&lt;Unit&gt;</c>, and behaviors see <c>TResponse = Unit</c>.
/// </summary>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker interface: generated code dispatches on it.")]
public interface IRequest : IRequest<Unit>;

/// <summary>
///     Marks a stream request: a message sent with <see cref="ISender.Stream{TItem}" /> that has exactly one handler, which returns an
///     <see cref="IAsyncEnumerable{T}" /> of <typeparamref name="TItem" />.
/// </summary>
/// <remarks>
///     A stream request is a non-abstract, non-generic class, record or struct that implements exactly one closing of
///     <see cref="IStreamRequest{TItem}" /> and no closing of <see cref="IRequest{TResponse}" />.
/// </remarks>
/// <typeparam name="TItem">The type of each streamed item.</typeparam>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker interface: generated code dispatches on it.")]
public interface IStreamRequest<TItem>;

/// <summary>
///     Marks a notification: a message sent with <see cref="IPublisher.Publish{TNotification}" /> that has zero or more handlers.
/// </summary>
/// <remarks>
///     A notification is a non-abstract, non-generic class, record or struct that implements <see cref="INotification" /> and is not also a
///     request. Handlers typed as a base class or interface that derives from <see cref="INotification" /> (or <see cref="INotification" />
///     itself) receive every concrete notification the host knows that converts to that type.
/// </remarks>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker interface: generated code dispatches on it.")]
public interface INotification;
