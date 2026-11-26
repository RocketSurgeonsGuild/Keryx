namespace Keryx;

/// <summary>
///     Sets the position of a behavior class in every pipeline it is part of. A behavior without it is in <see cref="Phase.Default" />
///     with order 0.
/// </summary>
/// <param name="phase">The phase of the behavior.</param>
[AttributeUsage(AttributeTargets.Class)]
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
///     Places this behavior class after (inside) another behavior that has the same phase and order. An open type such as
///     <c>typeof(X&lt;,&gt;)</c> matches every closing of <c>X</c>. An edge to a behavior that is not in a pipeline is ignored there.
/// </summary>
/// <remarks>An edge that contradicts phase or order, or a cycle, is KRX008.</remarks>
/// <param name="behaviorType">The behavior this one runs after.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RunsAfterAttribute(Type behaviorType) : Attribute
{
    /// <summary>
    ///     The behavior this one runs after.
    /// </summary>
    public Type BehaviorType { get; } = behaviorType;
}

/// <summary>
///     Places this behavior class after (inside) <typeparamref name="TBehavior" />, which has the same phase and order. Keryx treats it exactly
///     like <c>[RunsAfter(typeof(TBehavior))]</c>. An edge to a behavior that is not in a pipeline is ignored there.
/// </summary>
/// <remarks>
///     C# does not allow an unbound generic type as a type argument, so this form names only closed or non-generic behaviors. Use
///     <see cref="RunsAfterAttribute" /> with <c>typeof(X&lt;,&gt;)</c> to match every closing of an open behavior. An edge that contradicts
///     phase or order, or a cycle, is KRX008.
/// </remarks>
/// <typeparam name="TBehavior">The behavior this one runs after.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RunsAfterAttribute<TBehavior> : Attribute
{
    /// <summary>
    ///     The behavior this one runs after: <typeparamref name="TBehavior" />.
    /// </summary>
    public Type BehaviorType => typeof(TBehavior);
}

/// <summary>
///     Places this behavior class before (outside) another behavior that has the same phase and order. An open type such as
///     <c>typeof(X&lt;,&gt;)</c> matches every closing of <c>X</c>. An edge to a behavior that is not in a pipeline is ignored there.
/// </summary>
/// <remarks>An edge that contradicts phase or order, or a cycle, is KRX008.</remarks>
/// <param name="behaviorType">The behavior this one runs before.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RunsBeforeAttribute(Type behaviorType) : Attribute
{
    /// <summary>
    ///     The behavior this one runs before.
    /// </summary>
    public Type BehaviorType { get; } = behaviorType;
}

/// <summary>
///     Places this behavior class before (outside) <typeparamref name="TBehavior" />, which has the same phase and order. Keryx treats it
///     exactly like <c>[RunsBefore(typeof(TBehavior))]</c>. An edge to a behavior that is not in a pipeline is ignored there.
/// </summary>
/// <remarks>
///     C# does not allow an unbound generic type as a type argument, so this form names only closed or non-generic behaviors. Use
///     <see cref="RunsBeforeAttribute" /> with <c>typeof(X&lt;,&gt;)</c> to match every closing of an open behavior. An edge that contradicts
///     phase or order, or a cycle, is KRX008.
/// </remarks>
/// <typeparam name="TBehavior">The behavior this one runs before.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RunsBeforeAttribute<TBehavior> : Attribute
{
    /// <summary>
    ///     The behavior this one runs before: <typeparamref name="TBehavior" />.
    /// </summary>
    public Type BehaviorType => typeof(TBehavior);
}
