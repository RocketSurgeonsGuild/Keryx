using System.ComponentModel;
#if NET
using System.Runtime.CompilerServices;
#endif

namespace Keryx.Internal;

/// <summary>
///     Adapts a handler of an <see cref="IRequest" /> that returns a non-generic <see cref="ValueTask" /> or <see cref="Task" /> to the
///     <c>ValueTask&lt;Unit&gt;</c> every pipeline produces. Used by generated code only.
/// </summary>
/// <remarks>
///     A source that already completed successfully is converted without allocating. Otherwise it is awaited through an async helper that
///     uses <c>PoolingAsyncValueTaskMethodBuilder</c> on .NET 10 and later, and the plain builder on .NET Standard 2.0.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class UnitAdapter
{
    /// <summary>
    ///     Converts a <see cref="ValueTask" /> to a <c>ValueTask&lt;Unit&gt;</c>.
    /// </summary>
    /// <param name="source">The handler's task. It is consumed exactly once.</param>
    /// <returns>A task that completes with <see cref="Unit.Value" /> when <paramref name="source" /> does, or faults the same way.</returns>
    public static ValueTask<Unit> ToUnit(ValueTask source)
    {
        if (source.IsCompletedSuccessfully)
        {
            // Observes the result, which releases a pooled IValueTaskSource behind the ValueTask.
            source.GetAwaiter().GetResult();
            return new(Unit.Value);
        }

        return AwaitAsync(source);
    }

    /// <summary>
    ///     Converts a <see cref="Task" /> to a <c>ValueTask&lt;Unit&gt;</c>.
    /// </summary>
    /// <param name="source">The handler's task.</param>
    /// <returns>A task that completes with <see cref="Unit.Value" /> when <paramref name="source" /> does, or faults the same way.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source" /> is <see langword="null" />.</exception>
    public static ValueTask<Unit> ToUnit(Task source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Status == TaskStatus.RanToCompletion ? new(Unit.Value) : AwaitAsync(source);
    }

#if NET
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    private static async ValueTask<Unit> AwaitAsync(ValueTask source)
    {
        await source.ConfigureAwait(false);
        return Unit.Value;
    }

#if NET
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    private static async ValueTask<Unit> AwaitAsync(Task source)
    {
        await source.ConfigureAwait(false);
        return Unit.Value;
    }
}
