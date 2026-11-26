using System.Runtime.CompilerServices;

namespace Keryx.Benchmarks.Common;

/// <summary>The stream every stream handler returns.</summary>
public static class Numbers
{
    /// <summary>Items per stream in the stream scenarios.</summary>
    public const int StreamLength = 100;

    /// <summary>Yields 0 to <paramref name="count" /> - 1, completing synchronously.</summary>
    public static async IAsyncEnumerable<int> Range(int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return i;
        }
    }
}
