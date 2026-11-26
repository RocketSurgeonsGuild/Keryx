using System.Globalization;
using System.Text;
using Keryx.Analyzers.Hosts;
using Keryx.Tests.Behaviors;
using Keryx.Tests.Hosts;
using Microsoft.CodeAnalysis;

namespace Keryx.Tests.Streams;

/// <summary>
///     Snapshots of generated stream pipelines (ADR 0001, Streaming): pipelines of static stream behaviors at depths 0, 1, 3 and 8, with their
///     <c>StreamLink{k}</c> structs, the stream behavior methods called directly with explicit type arguments and their injected services held
///     by the pipeline context; and stream dispatch above the dispatch threshold.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class StreamGeneratorTests : HostTest
{
    private const string Behaviors = """
        public interface IAudit { void Write(string text); }

        [StreamBehavior]
        [BehaviorOrder(Phase.Observability)]
        public static class Trace
        {
            public static async IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(
                TRequest request,
                IAudit audit,
                TNext next,
                [EnumeratorCancellation] CancellationToken cancellationToken
            )
                where TRequest : IStreamRequest<TItem>
                where TNext : struct, IStreamNext<TItem>
            {
                audit.Write("start");
                await foreach (var item in next.InvokeAsync(cancellationToken))
                {
                    yield return item;
                }
            }
        }

        [StreamBehavior]
        [BehaviorOrder(Phase.Caching)]
        public static class Cache
        {
            public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IStreamRequest<TItem>
                where TNext : struct, IStreamNext<TItem> => next.InvokeAsync(cancellationToken);
        }

        [StreamBehavior]
        [BehaviorOrder(Phase.Transaction)]
        public static class SkipNulls
        {
            public static async IAsyncEnumerable<string?> HandleAsync<TNext>(Orders request, TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
                where TNext : struct, IStreamNext<string?>
            {
                await foreach (var item in next.InvokeAsync(cancellationToken))
                {
                    if (item is not null)
                    {
                        yield return item;
                    }
                }
            }
        }

        public sealed record Orders(int Count) : IStreamRequest<string?>;


        """;

    [Test]
    public async Task Should_Generate_A_Stream_Pipeline(CancellationToken cancellationToken) =>
        await VerifyHost(
            """
            public partial class OrdersHandler
            {
                [Behavior(typeof(SkipNulls))]
                [Behavior(typeof(Cache))]
                [Behavior(typeof(Trace))]
                private static IAsyncEnumerable<string?> Handle(Orders request, IAudit audit, CancellationToken cancellationToken) => throw new NotSupportedException();
            }

            /// <summary>No behaviors: the handler's enumerable is returned as is, through the typed overload without boxing.</summary>
            public readonly record struct Ticks(int Count) : IStreamRequest<int>;
            public static partial class TicksHandler
            {
                private static IAsyncEnumerable<int> Handle(Ticks request) => throw new NotSupportedException();
            }

            /// <summary>Not sealed: the typed overload checks the runtime type (OQ-31).</summary>
            public record Shapes : IStreamRequest<int>;
            public static partial class ShapesHandler
            {
                [Behavior(typeof(Trace))]
                private static IAsyncEnumerable<int> Handle(Shapes request) => throw new NotSupportedException();
            }
            """,
            cancellationToken
        );

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(3)]
    [Arguments(8)]
    public async Task Should_Generate_A_Stream_Pipeline_Of_Depth(int depth, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder("using System.Runtime.CompilerServices;\n\nnamespace Sample;\n\npublic interface IClock { }\n\n");
        for (var k = 1; k <= depth; k++)
        {
            var clock = k % 2 == 1 ? "IClock clock, " : "";
            builder.Append(
                CultureInfo.InvariantCulture,
                $$"""
                  [StreamBehavior]
                  [BehaviorOrder(Phase.Default, Order = {{k}})]
                  public static class Step{{k}}
                  {
                      public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest request, TNext next, {{clock}}CancellationToken cancellationToken)
                          where TRequest : IStreamRequest<TItem>
                          where TNext : struct, IStreamNext<TItem> => next.InvokeAsync(cancellationToken);
                  }


                  """
            );
        }

        builder.Append("public sealed record Numbers(int Count) : IStreamRequest<int>;\n\npublic static partial class NumbersHandler\n{\n");
        for (var k = depth; k >= 1; k--)
        {
            builder.Append(CultureInfo.InvariantCulture, $"    [Behavior(typeof(Step{k}))]\n");
        }

        builder.Append("    private static IAsyncEnumerable<int> Handle(Numbers request) => throw new NotSupportedException();\n}\n");
        var result = await HostBuilder.AddSources(Usings + builder).Build().GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        var addKeryx = PipelineGeneratorTests.Source(result, HostEmitter.ServiceCollectionExtensionsHintName);
        await Assert.That(addKeryx).DoesNotContain("Step");
        await Verify(PipelineGeneratorTests.Source(result, HostEmitter.MediatorHintName)).UseParameters(depth);
    }

    [Test]
    public async Task Should_Dispatch_Streams_Through_A_FrozenDictionary_Above_The_Threshold(CancellationToken cancellationToken)
    {
        var builder = new StringBuilder("namespace Sample;\n\n");
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
        var result = await HostBuilder.AddSources(Usings + "using System.Runtime.CompilerServices;\n\nnamespace Sample;\n\n" + Behaviors + handlers)
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
