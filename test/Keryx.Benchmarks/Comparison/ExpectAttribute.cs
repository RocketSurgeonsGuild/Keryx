namespace Keryx.Benchmarks.Comparison;

/// <summary>What <c>--verify</c> expects from every benchmark of a comparison scenario (see <see cref="Verification" />).</summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class ExpectAttribute : Attribute
{
    /// <summary>The value each benchmark returns (a real-world scenario's <c>Pong.Id</c>); -1 when it returns none (publish).</summary>
    public int Result { get; init; } = -1;

    /// <summary>The <c>Pong.Name</c> each real-world benchmark returns; <see langword="null" /> for the scenarios that return an <c>int</c>.</summary>
    public string? Name { get; init; }

    /// <summary>
    ///     Probe hits per benchmark call (<c>Probe.Hit</c>): handler and behavior calls, and in the real-world scenarios also each validator
    ///     call, repository lookup, SQLite query and formatted log message (a real-world notification handler makes 3: itself, its lookup or
    ///     query, its log message).
    /// </summary>
    public int Hits { get; init; }

    /// <summary>
    ///     Hits the baseline makes, when they differ from <see cref="Hits" />: the baseline never runs behaviors (in the real-world behavior
    ///     scenarios it does their work inline, so only the behavior calls are missing).
    /// </summary>
    public int BaselineHits { get; init; } = -1;
}
