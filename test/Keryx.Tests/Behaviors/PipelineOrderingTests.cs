using System.Text;
using Keryx.Analyzers.Hosts;
using Keryx.Tests.Hosts;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Tests.Behaviors;

/// <summary>
///     Every sort rule of ADR 0001, Ordering, observed at run time, with each <c>RunsAfter</c>/<c>RunsBefore</c> written in the <c>typeof</c>
///     form, the generic form, and both forms mixed on one class: phase ascending (including a value between phases), order inside a phase,
///     edges inside a (phase, order) bucket, the ordinal type name tie-break, edges that agree with phase as no-ops, edges to behaviors absent
///     from a pipeline, an open <c>typeof(X&lt;,&gt;)</c> matching every closing and a closed edge matching only its closing.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class PipelineOrderingTests : HostTest
{
    /// <summary>The expected order of behaviors per request, outermost first; the same for every form.</summary>
    private static readonly (string Request, string[] Order)[] Expected =
    [
        ("R1", ["PhaseObs", "PhaseCustom", "PhaseVal", "PhaseTx"]),
        ("R2", ["OrderMinus", "OrderOne", "OrderTwo"]),
        ("R3", ["EdgeB", "EdgeC", "EdgeA"]),
        ("R4", ["TieA", "TieB"]),
        ("R5", ["AgreeOuter", "AgreeInner"]),
        ("R6", ["AbsentA", "AbsentB"]),
        ("R7a", ["Wrap", "Inner"]),
        ("R7b", ["Wrap", "Inner"]),
        ("R8a", ["WrapC", "InnerC"]),
        ("R8b", ["InnerC", "WrapC"]),
    ];

    [Test]
    [Arguments(OrderingForm.TypeOf)]
    [Arguments(OrderingForm.Generic)]
    [Arguments(OrderingForm.Mixed)]
    public async Task Pipelines_Run_In_Sort_Order(OrderingForm form, CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(OrderingForms.Expand(Source, form), cancellationToken);
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        foreach (var (request, order) in Expected)
        {
            _ = Call<string[]>(host, "Sample.Log", "Take");
            await Assert.That(await sender.Send((IRequest<string>)Create(host, "Sample." + request), cancellationToken)).IsEqualTo(request);
            await Assert.That(Call<string[]>(host, "Sample.Log", "Take"))
                        .IsEquivalentTo([.. order, "handler"], TUnit.Assertions.Enums.CollectionOrdering.Matching)
                        .Because(request);
        }
    }

    [Test]
    public async Task Every_Form_Generates_The_Same_Mediator(CancellationToken cancellationToken)
    {
        var sources = new List<string>();
        foreach (var form in (OrderingForm[])[OrderingForm.TypeOf, OrderingForm.Generic, OrderingForm.Mixed])
        {
            var result = await HostBuilder.WithProjectName("Forms" + form).AddSources(Usings + OrderingForms.Expand(Source, form)).Build().GenerateAsync(cancellationToken);
            await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
            sources.Add(PipelineGeneratorTests.Source(result, HostEmitter.MediatorHintName));
        }

        await Assert.That(sources[1]).IsEqualTo(sources[0]);
        await Assert.That(sources[2]).IsEqualTo(sources[0]);
    }

    [Test]
    public async Task The_Forms_Expand_As_Expected(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string source = "{{After:A}}{{Before:B<C, D>}}{{After:E}}";
        await Assert.That(OrderingForms.Expand(source, OrderingForm.TypeOf)).IsEqualTo("[RunsAfter(typeof(A))][RunsBefore(typeof(B<C, D>))][RunsAfter(typeof(E))]");
        await Assert.That(OrderingForms.Expand(source, OrderingForm.Generic)).IsEqualTo("[RunsAfter<A>][RunsBefore<B<C, D>>][RunsAfter<E>]");
        await Assert.That(OrderingForms.Expand(source, OrderingForm.Mixed)).IsEqualTo("[RunsAfter(typeof(A))][RunsBefore<B<C, D>>][RunsAfter(typeof(E))]");
    }

    private static readonly string Source = BuildSource();

    private static string BuildSource()
    {
        var builder = new StringBuilder(
            """
            [assembly: KeryxHost]

            namespace Sample;

            public static class Log
            {
                public static readonly List<string> Entries = [];
                public static void Write(string entry) { lock (Entries) { Entries.Add(entry); } }
                public static string[] Take() { lock (Entries) { var entries = Entries.ToArray(); Entries.Clear(); return entries; } }
            }


            """
        );

        // R1: phase ascending, including a value between two phases.
        builder.Append(Closed("PhaseTx", "R1", "[BehaviorOrder(Phase.Transaction)]"));
        builder.Append(Closed("PhaseObs", "R1", "[BehaviorOrder(Phase.Observability)]"));
        builder.Append(Closed("PhaseVal", "R1", "[BehaviorOrder(Phase.Validation)]"));
        builder.Append(Closed("PhaseCustom", "R1", "[BehaviorOrder((Phase)250)]"));
        builder.Append(Handler("R1", "PhaseTx", "PhaseVal", "PhaseCustom", "PhaseObs"));

        // R2: order inside a phase.
        builder.Append(Closed("OrderTwo", "R2", "[BehaviorOrder(Phase.Default, Order = 2)]"));
        builder.Append(Closed("OrderOne", "R2", "[BehaviorOrder(Phase.Default, Order = 1)]"));
        builder.Append(Closed("OrderMinus", "R2", "[BehaviorOrder(Phase.Default, Order = -1)]"));
        builder.Append(Handler("R2", "OrderTwo", "OrderOne", "OrderMinus"));

        // R3: edges inside one bucket override name order (A, B, C); EdgeA carries two edges (one per form when mixed).
        builder.Append(Closed("EdgeA", "R3", "{{After:EdgeC}}\n{{After:EdgeB}}"));
        builder.Append(Closed("EdgeB", "R3", "{{Before:EdgeC}}"));
        builder.Append(Closed("EdgeC", "R3"));
        builder.Append(Handler("R3", "EdgeA", "EdgeC", "EdgeB"));

        // R4: no position and no edges: ordinal type name.
        builder.Append(Closed("TieB", "R4"));
        builder.Append(Closed("TieA", "R4"));
        builder.Append(Handler("R4", "TieB", "TieA"));

        // R5: edges that agree with phase change nothing (name order alone would put AgreeInner first).
        builder.Append(Closed("AgreeInner", "R5", "[BehaviorOrder(Phase.Transaction)]\n{{After:AgreeOuter}}"));
        builder.Append(Closed("AgreeOuter", "R5", "[BehaviorOrder(Phase.Observability)]\n{{Before:AgreeInner}}"));
        builder.Append(Handler("R5", "AgreeInner", "AgreeOuter"));

        // R6: an edge to a behavior that is not in this pipeline (TieA is R4's) is ignored.
        builder.Append(Closed("AbsentB", "R6", "{{Before:TieA}}"));
        builder.Append(Closed("AbsentA", "R6", "{{After:TieB}}"));
        builder.Append(Handler("R6", "AbsentB", "AbsentA"));

        // R7: an open typeof(X<,>) edge matches every closing (only the typeof form can name an open behavior).
        builder.Append(Open("Wrap"));
        builder.Append(Open("Inner", "[RunsAfter(typeof(Wrap<,>))]"));
        builder.Append(Handler("R7a", "Inner<,>", "Wrap<,>"));
        builder.Append(Handler("R7b", "Inner<,>", "Wrap<,>"));

        // R8: a closed edge matches only that closing, in either form.
        builder.Append(Open("WrapC"));
        builder.Append(Open("InnerC", "{{After:WrapC<R8a, string>}}"));
        builder.Append(Handler("R8a", "InnerC<,>", "WrapC<,>"));
        builder.Append(Handler("R8b", "InnerC<,>", "WrapC<,>"));
        return builder.ToString();
    }

    private static string Closed(string name, string request, string attributes = "") =>
        $$"""
          {{attributes}}
          public sealed class {{name}} : IRequestPipelineBehavior<{{request}}, string>
          {
              public ValueTask<string> Handle<TNext>({{request}} request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<string>
              {
                  Log.Write("{{name}}");
                  return next.InvokeAsync(cancellationToken);
              }
          }


          """;

    private static string Open(string name, string attributes = "") =>
        $$"""
          {{attributes}}
          public sealed class {{name}}<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
          {
              public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse>
              {
                  Log.Write("{{name}}");
                  return next.InvokeAsync(cancellationToken);
              }
          }


          """;

    private static string Handler(string request, params string[] behaviors)
    {
        var builder = new StringBuilder($"public sealed record {request} : IRequest<string>;\npublic static partial class {request}Handler\n{{\n");
        foreach (var behavior in behaviors)
        {
            builder.Append("    [Behavior(typeof(").Append(behavior).Append("))]\n");
        }

        builder.Append("    private static string Handle(").Append(request).Append(" request)\n    {\n        Log.Write(\"handler\");\n        return \"")
               .Append(request)
               .Append("\";\n    }\n}\n\n");
        return builder.ToString();
    }
}
