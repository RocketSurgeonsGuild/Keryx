using System.Text;
using Keryx.Analyzers.Hosts;
using Keryx.Tests.Behaviors;
using Keryx.Tests.Hosts;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions.Enums;
using static Keryx.Tests.Streams.StreamSources;

namespace Keryx.Tests.Streams;

/// <summary>
///     Stream behaviors sorted like request behaviors (ADR 0001, Streaming; Ordering): by <c>Phase</c>, by <c>Order</c> inside a phase and by
///     <c>typeof</c> <c>RunsAfter</c>/<c>RunsBefore</c> edges inside a (phase, order) bucket, with a behavior method's <c>[BehaviorOrder]</c>
///     overriding its class's; one link per stream behavior, outermost first.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class StreamOrderingTests : HostTest
{
    /// <summary>
    ///     The sorted pipeline: <c>WInner</c> (its method moves it to <c>Phase.Outermost</c>), <c>WObs</c>, <c>WSec</c>, the (Validation, 1)
    ///     bucket <c>WVal1</c>, <c>WValB</c>, <c>WVal</c> (by name alone <c>WVal</c> would be first; <c>WValB</c> runs before it), the Default
    ///     bucket <c>WDef2</c>, <c>WDef1</c> (reversed by an edge), then <c>WTx</c>.
    /// </summary>
    private static readonly string[] Order = ["WInner", "WObs", "WSec", "WVal1", "WValB", "WVal", "WDef2", "WDef1", "WTx"];

    [Test]
    public async Task Stream_Behaviors_Run_In_Sort_Order(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        _ = TakeLog(host);

        var (items, error) = await ConsumeAsync(
            scope.ServiceProvider.GetRequiredService<ISender>().Stream((IStreamRequest<int>)Create(host, "Sample.Interleaved", 1), cancellationToken),
            cancellationToken
        );

        await Assert.That(error).IsNull();
        await Assert.That(items.Count).IsEqualTo(1);
        await Assert.That(TakeLog(host))
                    .IsEquivalentTo([.. Order.Select(static b => b + ">"), "handler>", "<handler", .. Enumerable.Reverse(Order).Select(static b => "<" + b)], CollectionOrdering.Matching);
    }

    [Test]
    public async Task One_Link_Per_Stream_Behavior_Outermost_First(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + Source).Build().GenerateAsync(cancellationToken);
        result.AssertCompilationWasSuccessful();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();

        // The links, as their generated documentation states them.
        var links = PipelineGeneratorTests.Source(result, HostEmitter.MediatorHintName)
                                          .Split('\n')
                                          .Select(static line => line.Trim())
                                          .Where(static line => line.StartsWith("/// <summary>The <c>next</c> of", StringComparison.Ordinal))
                                          .ToArray();
        await Assert.That(links)
                    .IsEquivalentTo(
                         [
                             .. Order.Zip(Order.Skip(1)).Select(static pair => $"/// <summary>The <c>next</c> of <c>Sample.{pair.First}</c>: <c>Sample.{pair.Second}</c>.</summary>"),
                             $"/// <summary>The <c>next</c> of <c>Sample.{Order[^1]}</c>: the handler.</summary>",
                         ],
                         CollectionOrdering.Matching
                     );
    }

    private static readonly string Source = BuildSource();

    private static string BuildSource()
    {
        var builder = new StringBuilder("using System.Runtime.CompilerServices;\n\n[assembly: KeryxHost]\n\nnamespace Sample;\n\n").Append(Support);
        builder.Append(Closed("WObs", "[BehaviorOrder(Phase.Observability)]"));
        builder.Append(Closed("WSec", "[BehaviorOrder(Phase.Security)]"));
        builder.Append(Closed("WVal1", "[BehaviorOrder(Phase.Validation, Order = 1)]"));
        builder.Append(Closed("WVal", "[BehaviorOrder(Phase.Validation, Order = 1)]"));
        builder.Append(Closed("WValB", "[BehaviorOrder(Phase.Validation, Order = 1)]\n[RunsBefore(typeof(WVal))]"));
        builder.Append(Closed("WDef1", "[RunsAfter(typeof(WDef2))]"));
        builder.Append(Closed("WDef2", ""));
        builder.Append(Closed("WTx", "[BehaviorOrder(Phase.Transaction)]"));
        builder.Append(Closed("WInner", "[BehaviorOrder(Phase.Innermost)]", "[BehaviorOrder(Phase.Outermost)]"));
        builder.Append(Request("Interleaved", "WInner", "WTx", "WDef2", "WDef1", "WValB", "WVal", "WVal1", "WSec", "WObs"));
        return builder.ToString();
    }

    private static string Closed(string name, string attributes, string methodAttributes = "") =>
        $$"""
          [StreamBehavior]
          {{attributes}}
          public static class {{name}}
          {
              {{methodAttributes}}
              public static async IAsyncEnumerable<int> Handle<TNext>(Interleaved request, TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
                  where TNext : struct, IStreamNext<int>
              {
                  Log.Write("{{name}}>");
                  await foreach (var item in next.InvokeAsync(cancellationToken))
                  {
                      yield return item;
                  }

                  Log.Write("<{{name}}");
              }
          }


          """;
}
