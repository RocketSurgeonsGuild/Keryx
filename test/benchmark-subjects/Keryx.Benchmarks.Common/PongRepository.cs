namespace Keryx.Benchmarks.Common;

/// <summary>The scoped repository the real-world behavior scenario's handler injects.</summary>
public interface IPongRepository
{
    /// <summary>The name of the pong with <paramref name="id" />, or <see langword="null" />.</summary>
    string? FindName(int id);
}

/// <summary>A dictionary lookup, registered scoped as a typical repository is. Each lookup is a <see cref="Probe" /> hit.</summary>
public sealed class PongRepository : IPongRepository
{
    private static readonly Dictionary<int, string> Names = Enumerable.Range(1, PongCatalog.Count).ToDictionary(static id => id, PongCatalog.Name);

    /// <inheritdoc />
    public string? FindName(int id)
    {
        Probe.Hit();
        return Names.GetValueOrDefault(id);
    }
}
