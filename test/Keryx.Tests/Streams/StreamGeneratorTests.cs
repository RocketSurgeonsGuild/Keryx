using System.Globalization;
using System.Text;
using Keryx.Analyzers.Hosts;
using Keryx.Tests.Behaviors;
using Keryx.Tests.Hosts;
using Microsoft.CodeAnalysis;

namespace Keryx.Tests.Streams;

/// <summary>
///     Snapshots of generated stream pipelines (ADR 0001, Streaming; the Phase 5 brief): wrap-only, hook-only and mixed pipelines, with their
///     <c>StreamLink{k}</c> structs, one fused hook segment iterator per run of adjacent hook behaviors, and <c>Handle</c> called on the
///     concrete type; and stream dispatch above the dispatch threshold.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class StreamGeneratorTests : HostTest
{
    private const string Behaviors = """
        public interface IAudit { void Write(string text); }

        [BehaviorOrder(Phase.Observability)]
        public sealed class Trace<TRequest, TItem>(IAudit audit) : IStreamPipelineBehavior<TRequest, TItem> where TRequest : IStreamRequest<TItem>
        {
            public async IAsyncEnumerable<TItem> Handle<TNext>(TRequest request, TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
                where TNext : struct, IStreamNext<TItem>
            {
                audit.Write("start");
                await foreach (var item in next.InvokeAsync(cancellationToken))
                {
                    yield return item;
                }
            }
        }

        [BehaviorOrder(Phase.Caching)]
        public sealed class Cache<TRequest, TItem> : IStreamPipelineBehavior<TRequest, TItem> where TRequest : IStreamRequest<TItem>
        {
            public IAsyncEnumerable<TItem> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TNext : struct, IStreamNext<TItem> => next.InvokeAsync(cancellationToken);
        }

        [BehaviorOrder(Phase.Security)]
        public sealed class Authorize<TRequest, TItem> : IStreamHookBehavior<TRequest, TItem> where TRequest : IStreamRequest<TItem>
        {
            public ValueTask OnStartAsync(TRequest request, CancellationToken cancellationToken) => default;
            public bool OnItem(TRequest request, ref TItem item) => true;
            public ValueTask OnCompleteAsync(TRequest request) => default;
            public ValueTask OnErrorAsync(TRequest request, Exception exception) => default;
        }

        [BehaviorOrder(Phase.Validation)]
        public sealed class Validate<TRequest, TItem> : IStreamHookBehavior<TRequest, TItem> where TRequest : IStreamRequest<TItem>
        {
            public ValueTask OnStartAsync(TRequest request, CancellationToken cancellationToken) => default;
            public bool OnItem(TRequest request, ref TItem item) => item is not null;
            public ValueTask OnCompleteAsync(TRequest request) => default;
            public ValueTask OnErrorAsync(TRequest request, Exception exception) => default;
        }

        [BehaviorOrder(Phase.Transaction)]
        public sealed class Count(IAudit audit) : IStreamHookBehavior<Orders, string?>
        {
            public ValueTask OnStartAsync(Orders request, CancellationToken cancellationToken) => default;
            public bool OnItem(Orders request, ref string? item) => true;
            public ValueTask OnCompleteAsync(Orders request) { audit.Write("done"); return default; }
            public ValueTask OnErrorAsync(Orders request, Exception exception) => default;
        }

        public sealed record Orders(int Count) : IStreamRequest<string?>;


        """;

    [Test]
    public async Task Should_Generate_A_Wrap_Only_Stream_Pipeline(CancellationToken cancellationToken) =>
        await VerifyHost(
            """
            public partial class OrdersHandler
            {
                [Behavior(typeof(Cache<,>))]
                [Behavior(typeof(Trace<,>))]
                private static IAsyncEnumerable<string?> Handle(Orders request, IAudit audit, CancellationToken cancellationToken) => throw new NotSupportedException();
            }
            """,
            cancellationToken
        );

    [Test]
    public async Task Should_Generate_A_Hook_Only_Stream_Pipeline(CancellationToken cancellationToken) =>
        await VerifyHost(
            """
            public static partial class OrdersHandler
            {
                [Behavior(typeof(Count))]
                [Behavior(typeof(Validate<,>))]
                [Behavior(typeof(Authorize<,>))]
                private static async IAsyncEnumerable<string?> Handle(Orders request, [EnumeratorCancellation] CancellationToken cancellationToken)
                {
                    await Task.Yield();
                    yield return null;
                }
            }
            """,
            cancellationToken
        );

    /// <summary>Trace (wrap), Authorize and Validate (hooks), Cache (wrap), Count (hook): two segments split by Cache.</summary>
    [Test]
    public async Task Should_Generate_A_Mixed_Stream_Pipeline_With_A_Segment_Per_Run_Of_Hooks(CancellationToken cancellationToken) =>
        await VerifyHost(
            """
            public static partial class OrdersHandler
            {
                [Behavior(typeof(Count))]
                [Behavior(typeof(Cache<,>))]
                [Behavior(typeof(Validate<,>))]
                [Behavior(typeof(Authorize<,>))]
                [Behavior(typeof(Trace<,>))]
                private static IAsyncEnumerable<string?> Handle(Orders request) => throw new NotSupportedException();
            }

            /// <summary>No behaviors: the handler's enumerable is returned as is, through the typed overload without boxing.</summary>
            public readonly record struct Ticks(int Count) : IStreamRequest<int>;
            public static partial class TicksHandler
            {
                private static IAsyncEnumerable<int> Handle(Ticks request) => throw new NotSupportedException();
            }

            /// <summary>Not sealed: the typed overload checks the runtime type (OQ-31).</summary>
            public record Shapes : IStreamRequest<int>;
            public partial class ShapesHandler
            {
                [Behavior(typeof(Trace<,>))]
                private static IAsyncEnumerable<int> Handle(Shapes request, IAudit audit) => throw new NotSupportedException();
            }
            """,
            cancellationToken
        );

    [Test]
    public async Task Should_Dispatch_Streams_Through_A_FrozenDictionary_Above_The_Threshold(CancellationToken cancellationToken)
    {
        var builder = new StringBuilder("[assembly: KeryxHost]\n\nnamespace Sample;\n\n");
        for (var i = 0; i <= HostEmitter.DispatchThreshold; i++)
        {
            builder.Append(
                CultureInfo.InvariantCulture,
                $"public sealed record S{i} : IStreamRequest<int>;\npublic static partial class S{i}Handler {{ private static IAsyncEnumerable<int> Handle(S{i} request) => throw new NotSupportedException(); }}\n"
            );
        }

        var result = await HostBuilder.AddSources(Usings + builder).Build().GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        var mediator = PipelineGeneratorTests.Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("switch (__StreamRequestIndex(requestType))");
        await Assert.That(mediator).Contains("__streamRequestTypes.TryGetValue(requestType, out var requestIndex)");
        await Assert.That(mediator).DoesNotContain("__RequestIndex(");
    }

    private async Task VerifyHost(string handlers, CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + "using System.Runtime.CompilerServices;\n\n[assembly: KeryxHost]\n\nnamespace Sample;\n\n" + Behaviors + handlers)
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        await Verify(
            new
            {
                Mediator = PipelineGeneratorTests.Source(result, HostEmitter.MediatorHintName),
                AddKeryx = PipelineGeneratorTests.Source(result, HostEmitter.ServiceCollectionExtensionsHintName),
            }
        );
    }
}
