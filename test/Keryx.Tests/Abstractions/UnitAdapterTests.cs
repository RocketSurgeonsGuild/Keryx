using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;
using Keryx.Internal;

namespace Keryx.Tests.Abstractions;

[NotInParallel(nameof(UnitAdapterTests))]
public class UnitAdapterTests
{
    private const int Iterations = 1_000;

    [Test]
    public async Task Completed_ValueTask_Converts_Synchronously()
    {
        var result = UnitAdapter.ToUnit(default(ValueTask));

        await Assert.That(result.IsCompletedSuccessfully).IsTrue();
        await Assert.That(await result).IsEqualTo(Unit.Value);
    }

    [Test]
    public async Task Completed_Task_Converts_Synchronously()
    {
        var result = UnitAdapter.ToUnit(Task.CompletedTask);

        await Assert.That(result.IsCompletedSuccessfully).IsTrue();
        await Assert.That(await result).IsEqualTo(Unit.Value);
    }

    [Test]
    public async Task Completed_ValueTask_Allocates_Nothing()
    {
        var allocated = MeasureCompleted(static () => UnitAdapter.ToUnit(default(ValueTask)));

        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Completed_ValueTask_Backed_By_A_Source_Allocates_Nothing_And_Observes_The_Result()
    {
        var source = new CompletedValueTaskSource();
        var allocated = MeasureCompleted(() => UnitAdapter.ToUnit(source.Next()));

        await Assert.That(allocated).IsEqualTo(0L);
        await Assert.That(source.Unobserved).IsEqualTo(0);
    }

    [Test]
    public async Task Completed_Task_Allocates_Nothing()
    {
        var completed = Task.CompletedTask;
        var allocated = MeasureCompleted(() => UnitAdapter.ToUnit(completed));

        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Yielding_ValueTask_Completes_With_Unit()
    {
        var completed = false;

        async ValueTask Handler()
        {
            await Task.Yield();
            completed = true;
        }

        var result = UnitAdapter.ToUnit(Handler());

        await Assert.That(await result).IsEqualTo(Unit.Value);
        await Assert.That(completed).IsTrue();
    }

    [Test]
    public async Task Yielding_Task_Completes_With_Unit()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = UnitAdapter.ToUnit(completion.Task);

        await Assert.That(result.IsCompleted).IsFalse();
        completion.SetResult(true);
        await Assert.That(await result).IsEqualTo(Unit.Value);
    }

    [Test]
    public async Task Yielding_ValueTask_Converts_Repeatedly()
    {
        // Exercises the pooled builder (net10+) across reuse of its state machine boxes.
        for (var i = 0; i < 100; i++)
        {
            await Assert.That(await UnitAdapter.ToUnit(YieldAsync())).IsEqualTo(Unit.Value);
        }

        static async ValueTask YieldAsync() => await Task.Yield();
    }

    [Test]
    public async Task Faulted_ValueTask_Rethrows_The_Exception()
    {
        var failure = new InvalidOperationException("sync");

        await Assert.That(async () => await UnitAdapter.ToUnit(ValueTask.FromException(failure))).Throws<InvalidOperationException>()
                    .WithMessage("sync");
    }

    [Test]
    public async Task Yielding_Faulted_ValueTask_Rethrows_The_Exception()
    {
        static async ValueTask Handler()
        {
            await Task.Yield();
            throw new InvalidOperationException("async");
        }

        await Assert.That(async () => await UnitAdapter.ToUnit(Handler())).Throws<InvalidOperationException>().WithMessage("async");
    }

    [Test]
    public async Task Faulted_Task_Rethrows_The_Exception()
    {
        static async Task Handler()
        {
            await Task.Yield();
            throw new FormatException("task");
        }

        await Assert.That(async () => await UnitAdapter.ToUnit(Handler())).Throws<FormatException>().WithMessage("task");
    }

    [Test]
    public async Task Canceled_Task_Rethrows_Cancellation()
    {
        var canceled = Task.FromCanceled(new CancellationToken(true));
        var result = UnitAdapter.ToUnit(canceled);

        await Assert.That(result.IsCanceled).IsTrue();
        await Assert.That(async () => await result).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task Canceled_ValueTask_Rethrows_Cancellation()
    {
        var result = UnitAdapter.ToUnit(ValueTask.FromCanceled(new CancellationToken(true)));

        await Assert.That(result.IsCanceled).IsTrue();
        await Assert.That(async () => await result).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task Null_Task_Throws() => await Assert.That(async () => await UnitAdapter.ToUnit(null!)).Throws<ArgumentNullException>();

    // Measures a region that completes synchronously, after warm-up (ADR 0001, Performance contract).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureCompleted(Func<ValueTask<Unit>> convert)
    {
        for (var i = 0; i < Iterations; i++)
        {
            Consume(convert());
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
        {
            Consume(convert());
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Consume(ValueTask<Unit> result)
    {
        if (!result.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Expected a synchronously completed ValueTask<Unit>.");
        }

        _ = result.Result;
    }

    // A reusable, always-completed IValueTaskSource that counts results handed out but not yet observed.
    private sealed class CompletedValueTaskSource : IValueTaskSource
    {
        private short _token;

        public int Unobserved { get; private set; }

        public ValueTask Next()
        {
            Unobserved++;
            return new(this, ++_token);
        }

        public void GetResult(short token)
        {
            if (token != _token)
            {
                throw new InvalidOperationException("Stale token.");
            }

            Unobserved--;
        }

        public ValueTaskSourceStatus GetStatus(short token) => ValueTaskSourceStatus.Succeeded;

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            throw new NotSupportedException();
    }
}
