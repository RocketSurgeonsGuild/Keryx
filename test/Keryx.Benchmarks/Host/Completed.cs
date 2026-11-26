namespace Keryx.Benchmarks.Host;

/// <summary>Reads the result of a send the benchmark knows completed synchronously, and fails loudly if it did not.</summary>
internal static class Completed
{
    public static T Result<T>(ValueTask<T> task) =>
        task.IsCompletedSuccessfully ? task.Result : throw new InvalidOperationException("The benchmarked send did not complete synchronously.");
}
