namespace Keryx;

/// <summary>
///     The position of a behavior in a pipeline. Lower values run first, so the lowest phase is the outermost wrapper.
/// </summary>
/// <remarks>
///     The values leave gaps, so a behavior can sit between two phases, for example <c>(Phase)250</c>. Within a phase,
///     <see cref="BehaviorOrderAttribute.Order" />, then <see cref="RunsAfterAttribute" />/<see cref="RunsBeforeAttribute" />, then the
///     ordinal fully qualified type name decide the order.
/// </remarks>
public enum Phase
{
    /// <summary>
    ///     The outermost phase, for exception handling.
    /// </summary>
    Outermost = 0,

    /// <summary>
    ///     Logging, tracing and metrics.
    /// </summary>
    Observability = 100,

    /// <summary>
    ///     Authorization.
    /// </summary>
    Security = 200,

    /// <summary>
    ///     Validation.
    /// </summary>
    Validation = 300,

    /// <summary>
    ///     The phase of a behavior that declares none.
    /// </summary>
    Default = 400,

    /// <summary>
    ///     Caching: after <see cref="Default" /> and before <see cref="Transaction" />, so a cache hit never opens a transaction.
    /// </summary>
    Caching = 450,

    /// <summary>
    ///     Transactions and units of work.
    /// </summary>
    Transaction = 500,

    /// <summary>
    ///     The innermost phase, next to the handler.
    /// </summary>
    Innermost = 600,
}
