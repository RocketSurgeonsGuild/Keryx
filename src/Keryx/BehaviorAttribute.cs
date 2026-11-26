namespace Keryx;

/// <summary>
///     Attaches a behavior.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>On a request or stream request handler method: a request or stream behavior for that handler.</item>
///         <item>
///             On a notification handler method: an <see cref="INotificationHandlerPipelineBehavior{TNotification}" /> for that
///             handler.
///         </item>
///         <item>On a notification type: an <see cref="INotificationPipelineBehavior{TNotification}" /> for every publish of it.</item>
///         <item>On the assembly: applies to every matching target in that assembly.</item>
///     </list>
///     Anywhere else it is KRX042. A behavior attached to a method or notification type that does not match it is KRX009.
/// </remarks>
/// <param name="behaviorType">The behavior class, open (<c>typeof(AuditBehavior&lt;,&gt;)</c>) or closed.</param>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class BehaviorAttribute(Type behaviorType) : Attribute
{
    /// <summary>
    ///     The behavior class.
    /// </summary>
    public Type BehaviorType { get; } = behaviorType;
}

/// <summary>
///     Applies this behavior class to every matching target in every host that references it, from any referenced assembly.
/// </summary>
/// <remarks>
///     A host removes a global behavior with <see cref="ExcludeBehaviorAttribute" />. On a type that does not implement exactly one Keryx
///     behavior interface closing it is KRX042.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class GlobalBehaviorAttribute : Attribute;

/// <summary>
///     In a host, removes a behavior from every pipeline, whatever scope attached it.
/// </summary>
/// <remarks>A type that is not part of any pipeline is KRX036. Outside a host the attribute has no effect and is KRX043.</remarks>
/// <param name="behaviorType">The behavior class, open (<c>typeof(X&lt;,&gt;)</c>) or closed.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ExcludeBehaviorAttribute(Type behaviorType) : Attribute
{
    /// <summary>
    ///     The behavior class to remove.
    /// </summary>
    public Type BehaviorType { get; } = behaviorType;
}

/// <summary>
///     In a host, ignores a handler class from a referenced assembly, for example to replace it in a test host.
/// </summary>
/// <remarks>A type that is not part of any pipeline is KRX036. Outside a host the attribute has no effect and is KRX043.</remarks>
/// <param name="handlerType">The handler class to ignore.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ExcludeHandlerAttribute(Type handlerType) : Attribute
{
    /// <summary>
    ///     The handler class to ignore.
    /// </summary>
    public Type HandlerType { get; } = handlerType;
}
