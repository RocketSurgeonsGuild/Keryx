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
///     Wrap and hook behaviors sorted in one list (ADR 0001, Streaming; Ordering): they interleave by <c>Phase</c>, by <c>Order</c> inside a
///     phase and by <c>RunsAfter</c>/<c>RunsBefore</c> inside a (phase, order) bucket, each edge written in the <c>typeof</c> form, the generic
///     form and both mixed, with the same result; and the sorted list splits into fused hook segments at every wrap behavior.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class StreamOrderingTests : HostTest
{
    /// <summary>
    ///     The sorted pipeline: <c>HObs</c> | <c>WSec</c> | <c>HValB</c> | <c>WVal</c> | <c>HVal1</c>, <c>HVal2</c>, <c>HDef2</c>,
    ///     <c>HDef1</c> | <c>WTx</c> | <c>HInner</c>: four segments split by three wrap behaviors. By name alone the (Validation, 1) bucket would
    ///     be <c>HVal1</c>, <c>HValB</c>, <c>WVal</c> and the Default bucket <c>HDef1</c>, <c>HDef2</c>; the edges reverse them.
    /// </summary>
    private static readonly string[] ExpectedLog =
    [
        "HObs.start", "WSec>", "HValB.start", "WVal>", "HVal1.start", "HVal2.start", "HDef2.start", "HDef1.start", "WTx>", "HInner.start", "handler>",
        "HInner.item 1", "HDef1.item 1", "HDef2.item 1", "HVal2.item 1", "HVal1.item 1", "HValB.item 1", "HObs.item 1", "<handler", "HInner.complete", "<WTx",
        "HDef1.complete", "HDef2.complete", "HVal2.complete", "HVal1.complete", "<WVal", "HValB.complete", "<WSec", "HObs.complete",
    ];

    [Test]
    [Arguments(OrderingForm.TypeOf)]
    [Arguments(OrderingForm.Generic)]
    [Arguments(OrderingForm.Mixed)]
    public async Task Wrap_And_Hook_Behaviors_Run_In_Sort_Order(OrderingForm form, CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(OrderingForms.Expand(Source, form), cancellationToken);
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        _ = TakeLog(host);

        var (items, error) = await ConsumeAsync(
            scope.ServiceProvider.GetRequiredService<ISender>().Stream((IStreamRequest<int>)Create(host, "Sample.Interleaved", 1), cancellationToken),
            cancellationToken
        );

        await Assert.That(error).IsNull();
        await Assert.That(items.Count).IsEqualTo(1);
        await Assert.That(TakeLog(host)).IsEquivalentTo(ExpectedLog, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Every_Form_Generates_The_Same_Segment_Split(CancellationToken cancellationToken)
    {
        var sources = new List<string>();
        foreach (var form in (OrderingForm[])[OrderingForm.TypeOf, OrderingForm.Generic, OrderingForm.Mixed])
        {
            var result = await HostBuilder.WithProjectName("Forms" + form).AddSources(Usings + OrderingForms.Expand(Source, form)).Build().GenerateAsync(cancellationToken);
            result.AssertCompilationWasSuccessful();
            await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
            sources.Add(PipelineGeneratorTests.Source(result, HostEmitter.MediatorHintName));
        }

        await Assert.That(sources[1]).IsEqualTo(sources[0]);
        await Assert.That(sources[2]).IsEqualTo(sources[0]);

        // The split, as the generated documentation of each link and segment states it, outermost first.
        var split = string.Join(
            "\n",
            sources[0]
               .Split('\n')
               .Select(static line => line.Trim())
               .Where(static line => line.StartsWith("/// <summary>The fused hook segment", StringComparison.Ordinal)
                                  || line.StartsWith("/// <summary>The <c>next</c> of", StringComparison.Ordinal))
        );
        await Verify(split);
    }

    private static readonly string Source = BuildSource();

    private static string BuildSource()
    {
        var builder = new StringBuilder("using System.Runtime.CompilerServices;\n\n[assembly: KeryxHost]\n\nnamespace Sample;\n\n").Append(Support);
        builder.Append(ClosedHook("HObs", "[BehaviorOrder(Phase.Observability)]"));
        builder.Append(ClosedWrap("WSec", "[BehaviorOrder(Phase.Security)]"));
        builder.Append(ClosedHook("HVal1", "[BehaviorOrder(Phase.Validation, Order = 1)]"));
        builder.Append(ClosedWrap("WVal", "[BehaviorOrder(Phase.Validation, Order = 1)]\n{{Before:HVal1}}"));
        builder.Append(ClosedHook("HValB", "[BehaviorOrder(Phase.Validation, Order = 1)]\n{{Before:WVal}}"));
        builder.Append(ClosedHook("HVal2", "[BehaviorOrder(Phase.Validation, Order = 2)]"));
        builder.Append(ClosedHook("HDef1", "{{After:HDef2}}"));
        builder.Append(ClosedHook("HDef2", ""));
        builder.Append(ClosedWrap("WTx", "[BehaviorOrder(Phase.Transaction)]"));
        builder.Append(ClosedHook("HInner", "[BehaviorOrder(Phase.Innermost)]"));
        builder.Append(Request("Interleaved", "HInner", "WTx", "HDef2", "HDef1", "HVal2", "HValB", "WVal", "HVal1", "WSec", "HObs"));
        return builder.ToString();
    }

    private static string ClosedHook(string name, string attributes) =>
        $$"""
          {{attributes}}
          public sealed class {{name}} : IStreamHookBehavior<Interleaved, int>
          {
              public ValueTask OnStartAsync(Interleaved request, CancellationToken cancellationToken) { Log.Write("{{name}}.start"); return default; }
              public bool OnItem(Interleaved request, ref int item) { Log.Write("{{name}}.item " + item.ToString(System.Globalization.CultureInfo.InvariantCulture)); return true; }
              public ValueTask OnCompleteAsync(Interleaved request) { Log.Write("{{name}}.complete"); return default; }
              public ValueTask OnErrorAsync(Interleaved request, Exception exception) { Log.Write("{{name}}.error"); return default; }
          }


          """;

    private static string ClosedWrap(string name, string attributes) =>
        $$"""
          {{attributes}}
          public sealed class {{name}} : IStreamPipelineBehavior<Interleaved, int>
          {
              public async IAsyncEnumerable<int> Handle<TNext>(Interleaved request, TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
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
