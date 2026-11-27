namespace Keryx.Benchmarks.Common;

/// <summary>The response of every real-world scenario's <c>Ping</c> request, shared by every subject so each library returns the same type.</summary>
public sealed record Pong(int Id, string Name);

/// <summary>The pongs the scoped repository and the SQLite database hold, and the rule every <c>Ping</c> validator checks.</summary>
public static class PongCatalog
{
    /// <summary>The number of pongs: ids 1 to <see cref="Count" />.</summary>
    public const int Count = 100;

    /// <summary>The id every real-world benchmark sends.</summary>
    public const int BenchmarkId = 42;

    /// <summary>The stored name of <see cref="BenchmarkId" /> (<see cref="Name" /> of it).</summary>
    public const string BenchmarkName = "pong-42";

    /// <summary>The name the plain <c>Ping</c> handler returns, without a lookup.</summary>
    public const string PlainName = "pong";

    /// <summary>The stored name of the pong with <paramref name="id" />.</summary>
    public static string Name(int id) => "pong-" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The validation rule: the id must name a stored pong. Returns the error, or <see langword="null" /> when valid.</summary>
    public static string? CheckId(int id) => id is > 0 and <= Count ? null : "Id must be between 1 and 100.";
}
