namespace Keryx;

/// <summary>
///     A type with exactly one value. It is the response type of a request that returns nothing (<see cref="IRequest" />, which is
///     <see cref="IRequest{TResponse}" /> of <see cref="Unit" />), so one behavior shape covers requests with and without a response.
/// </summary>
public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>
{
    /// <summary>
    ///     The only value of <see cref="Unit" />.
    /// </summary>
    public static readonly Unit Value = default;

    /// <summary>
    ///     Compares two <see cref="Unit" /> values. Always <see langword="true" />.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true" />.</returns>
    public static bool operator ==(Unit left, Unit right) => left.Equals(right);

    /// <summary>
    ///     Compares two <see cref="Unit" /> values. Always <see langword="false" />.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="false" />.</returns>
    public static bool operator !=(Unit left, Unit right) => !left.Equals(right);

    /// <summary>
    ///     Compares this value with another <see cref="Unit" />. Always <see langword="true" />.
    /// </summary>
    /// <param name="other">The other value.</param>
    /// <returns><see langword="true" />.</returns>
    public bool Equals(Unit other) => true;

    /// <summary>
    ///     Returns <see langword="true" /> when <paramref name="obj" /> is a (boxed) <see cref="Unit" />.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true" /> when <paramref name="obj" /> is a <see cref="Unit" />; otherwise <see langword="false" />.</returns>
    public override bool Equals(object? obj) => obj is Unit;

    /// <summary>
    ///     Returns the hash code of the only value. Always 0.
    /// </summary>
    /// <returns>0.</returns>
    public override int GetHashCode() => 0;

    /// <summary>
    ///     Orders this value relative to another <see cref="Unit" />. Always 0, because every <see cref="Unit" /> is equal.
    /// </summary>
    /// <param name="other">The other value.</param>
    /// <returns>0.</returns>
    public int CompareTo(Unit other) => 0;

    /// <summary>
    ///     Returns <c>()</c>.
    /// </summary>
    /// <returns><c>()</c>.</returns>
    public override string ToString() => "()";
}
