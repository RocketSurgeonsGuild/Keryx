namespace Keryx.Benchmarks.Common;

/// <summary>
///     A message type's name, read once per type: what every real-world logging behavior, and the baseline with its concrete types, passes
///     to <see cref="RequestLog" /> (owner decision 36, open-questions OQ-75). Under the JIT, <c>typeof(T).Name</c> is cached and allocates
///     nothing; under NativeAOT every read allocates a new string (48 B for <c>TypicalPing</c>), which would put two allocations of the
///     benchmark's own code in every behavior row. Reading it here once, the same way in every subject, keeps that out of every row under
///     both runtimes.
/// </summary>
/// <typeparam name="T">The request or notification type.</typeparam>
public static class MessageName<T>
{
    /// <summary>The type's name (<see cref="Type.Name" />), read when the class is first used.</summary>
    public static readonly string Value = typeof(T).Name;
}
