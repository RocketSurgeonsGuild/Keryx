namespace Keryx;

/// <summary>
///     Sets the position of a behavior in every pipeline it is part of. On a behavior class it applies to every behavior method the class
///     declares; on a behavior method it overrides the class's for that method's kind. A behavior without it is in
///     <see cref="Phase.Default" /> with order 0.
/// </summary>
/// <param name="phase">The phase of the behavior.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class BehaviorOrderAttribute(Phase phase) : Attribute
{
    /// <summary>
    ///     The phase of the behavior. Lower phases run first (outermost).
    /// </summary>
    public Phase Phase { get; } = phase;

    /// <summary>
    ///     The order inside the phase, ascending. Default 0.
    /// </summary>
    public int Order { get; init; }
}

/// <summary>
///     Places this behavior after (inside) another behavior class that has the same phase and order. In a pipeline, <c>typeof(X)</c> means
///     <c>X</c>'s behavior of that pipeline's kind; an edge to a behavior class that has no behavior in a pipeline is ignored there.
/// </summary>
/// <remarks>
///     On a behavior class it applies to every behavior method the class declares; the <c>[RunsAfter]</c> list of a behavior method replaces
///     the class's for that method's kind. An edge that contradicts phase or order, or a cycle, is KRX008.
/// </remarks>
/// <param name="behaviorType">The behavior class this one runs after.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RunsAfterAttribute(Type behaviorType) : Attribute
{
    /// <summary>
    ///     The behavior class this one runs after.
    /// </summary>
    public Type BehaviorType { get; } = behaviorType;
}

/// <summary>
///     Places this behavior before (outside) another behavior class that has the same phase and order. In a pipeline, <c>typeof(X)</c> means
///     <c>X</c>'s behavior of that pipeline's kind; an edge to a behavior class that has no behavior in a pipeline is ignored there.
/// </summary>
/// <remarks>
///     On a behavior class it applies to every behavior method the class declares; the <c>[RunsBefore]</c> list of a behavior method
///     replaces the class's for that method's kind. An edge that contradicts phase or order, or a cycle, is KRX008.
/// </remarks>
/// <param name="behaviorType">The behavior class this one runs before.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RunsBeforeAttribute(Type behaviorType) : Attribute
{
    /// <summary>
    ///     The behavior class this one runs before.
    /// </summary>
    public Type BehaviorType { get; } = behaviorType;
}
