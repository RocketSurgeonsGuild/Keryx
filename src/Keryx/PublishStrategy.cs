namespace Keryx;

/// <summary>
///     How <see cref="IPublisher.Publish{TNotification}" /> runs the handlers of a notification.
/// </summary>
public enum PublishStrategy
{
    /// <summary>
    ///     Handlers are awaited one after another, in handler order. The default.
    /// </summary>
    Sequential,

    /// <summary>
    ///     Every handler pipeline is started in handler order on the calling thread without awaiting it, then the pending ones are awaited
    ///     together.
    /// </summary>
    Parallel,
}

/// <summary>
///     Sets the <see cref="PublishStrategy" /> of a notification type. Without it, publishing is
///     <see cref="PublishStrategy.Sequential" />.
/// </summary>
/// <remarks>On a type that is not a notification it is KRX041.</remarks>
/// <param name="strategy">The strategy.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class PublishStrategyAttribute(PublishStrategy strategy) : Attribute
{
    /// <summary>
    ///     The strategy.
    /// </summary>
    public PublishStrategy Strategy { get; } = strategy;
}

/// <summary>
///     Sets the order of a notification handler among the handlers of the same notification, ascending. Default 0; ties are broken by the
///     ordinal fully qualified handler class name. Exact-type and polymorphic handlers share one ordering.
/// </summary>
/// <remarks>On a method that is not a notification handler it is KRX040.</remarks>
/// <param name="order">The order.</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HandlerOrderAttribute(int order) : Attribute
{
    /// <summary>
    ///     The order.
    /// </summary>
    public int Order { get; } = order;
}
