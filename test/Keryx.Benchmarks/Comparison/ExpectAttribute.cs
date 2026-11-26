namespace Keryx.Benchmarks.Comparison;

/// <summary>What <c>--verify</c> expects from every benchmark of a comparison scenario (see <see cref="Verification" />).</summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class ExpectAttribute : Attribute
{
    /// <summary>The value each benchmark returns; -1 when it returns none (publish).</summary>
    public int Result { get; init; } = -1;

    /// <summary>Handler and behavior calls per benchmark call (<c>Probe.Hit</c>).</summary>
    public int Hits { get; init; }

    /// <summary>Calls the baseline makes, when it differs from <see cref="Hits" />: the baseline calls the handler only, never behaviors.</summary>
    public int BaselineHits { get; init; } = -1;
}
