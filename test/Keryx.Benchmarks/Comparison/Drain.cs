namespace Keryx.Benchmarks.Comparison;

/// <summary>Enumerates a stream to the end, as every stream benchmark does.</summary>
internal static class Drain
{
    public static async ValueTask<int> Sum(IAsyncEnumerable<int> items)
    {
        var sum = 0;
        await foreach (var item in items)
        {
            sum += item;
        }

        return sum;
    }
}
