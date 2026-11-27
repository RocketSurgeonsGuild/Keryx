namespace Keryx;

/// <summary>
///     Marks a <see langword="static" /> behavior class that declares a request behavior method: a <see langword="static" />
///     <c>Handle</c> or <c>HandleAsync</c> that wraps one <c>Send</c> of a request.
/// </summary>
/// <remarks>
///     The method takes the request, a <c>next</c> parameter whose type is a method type parameter constrained
///     <c>struct, INext&lt;TResponse&gt;</c>, an optional <see cref="CancellationToken" /> and injected services, and returns
///     <c>ValueTask&lt;TResponse&gt;</c>. It is fully open (<c>Handle&lt;TRequest, TResponse, TNext&gt;</c> with
///     <c>where TRequest : IRequest&lt;TResponse&gt;</c>) or fully closed (generic only in <c>TNext</c>). Any other shape is KRX019.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequestBehaviorAttribute : Attribute;

/// <summary>
///     Marks a <see langword="static" /> behavior class that declares a stream behavior method: a <see langword="static" /> <c>Handle</c> or
///     <c>HandleAsync</c> that wraps the inner stream of a stream request and returns the stream the outer behavior or the caller
///     enumerates.
/// </summary>
/// <remarks>
///     The method takes the stream request, a <c>next</c> parameter whose type is a method type parameter constrained
///     <c>struct, IStreamNext&lt;TItem&gt;</c>, an optional <see cref="CancellationToken" /> and injected services, and returns
///     <c>IAsyncEnumerable&lt;TItem&gt;</c>. Written as an <see langword="async" /> iterator, it puts <c>[EnumeratorCancellation]</c> on its
///     token and passes that token to <see cref="IStreamNext{TItem}.InvokeAsync" />. It is fully open
///     (<c>Handle&lt;TRequest, TItem, TNext&gt;</c> with <c>where TRequest : IStreamRequest&lt;TItem&gt;</c>) or fully closed. Any other
///     shape is KRX019.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class StreamBehaviorAttribute : Attribute;

/// <summary>
///     Marks a <see langword="static" /> behavior class that declares a notification behavior method: a <see langword="static" />
///     <c>Handle</c> or <c>HandleAsync</c> that wraps one whole <see cref="IPublisher.Publish{TNotification}" /> call, every handler and the
///     publish strategy included, also when the notification has no handlers.
/// </summary>
/// <remarks>
///     The method takes the notification (always the concrete published type), a <c>next</c> parameter whose type is a method type
///     parameter constrained <c>struct, INext</c>, an optional <see cref="CancellationToken" /> and injected services, and returns
///     <see cref="ValueTask" />. It is fully open (<c>Handle&lt;TNotification, TNext&gt;</c> with
///     <c>where TNotification : INotification</c>) or fully closed. Any other shape is KRX019. Notification handlers have no behaviors of
///     their own.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NotificationBehaviorAttribute : Attribute;

/// <summary>
///     Attaches a behavior class: its behavior method of the target's kind.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>On a request or stream request handler method: the class's request or stream behavior, for that handler.</item>
///         <item>On a notification type: the class's notification behavior, for every publish of it.</item>
///         <item>
///             On the assembly: applies to every matching target in that assembly (for a notification behavior, the notification types
///             declared there).
///         </item>
///     </list>
///     Anywhere else, including on a notification handler method, it is KRX042. A behavior attached to a handler method or notification type
///     that does not fit it is KRX009.
/// </remarks>
/// <param name="behaviorType">The behavior class.</param>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class BehaviorAttribute(Type behaviorType) : Attribute
{
    /// <summary>
    ///     The behavior class.
    /// </summary>
    public Type BehaviorType { get; } = behaviorType;
}

/// <summary>
///     Applies a behavior class's behaviors to every matching target in every host that references it, from any referenced assembly. On a
///     behavior method it makes only that method's kind global.
/// </summary>
/// <remarks>
///     A host removes a global behavior with <see cref="ExcludeBehaviorAttribute" />. On a class without a behavior kind attribute, or on a
///     method that is not a behavior method, it is KRX042.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class GlobalBehaviorAttribute : Attribute;

/// <summary>
///     In a host, removes a behavior class from every pipeline of every kind, whatever scope attached it.
/// </summary>
/// <remarks>A type that is not part of any pipeline is KRX036. Outside a host the attribute has no effect and is KRX043.</remarks>
/// <param name="behaviorType">The behavior class.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ExcludeBehaviorAttribute(Type behaviorType) : Attribute
{
    /// <summary>
    ///     The behavior class to remove.
    /// </summary>
    public Type BehaviorType { get; } = behaviorType;
}

/// <summary>
///     In a host, ignores a notification handler class from a referenced assembly, for example to replace it in a test host.
/// </summary>
/// <remarks>
///     Only notification handlers can be excluded: a request or stream request handler is KRX039 and stays in the host (a test swaps what it
///     does through the services it injects). A type that is not part of any pipeline is KRX036. Outside a host the attribute has no effect
///     and is KRX043.
/// </remarks>
/// <param name="handlerType">The notification handler class to ignore.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ExcludeHandlerAttribute(Type handlerType) : Attribute
{
    /// <summary>
    ///     The handler class to ignore.
    /// </summary>
    public Type HandlerType { get; } = handlerType;
}
