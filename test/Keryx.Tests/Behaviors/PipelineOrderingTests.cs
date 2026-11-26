using System.Text;
using Keryx.Analyzers.Hosts;
using Keryx.Tests.Hosts;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Tests.Behaviors;

/// <summary>
///     Every sort rule of ADR 0001, Ordering, observed at run time with static behaviors: phase ascending (including a value between phases),
///     order inside a phase, <c>typeof</c> edges inside a (phase, order) bucket, the ordinal class name tie-break, edges that agree with phase
///     as no-ops, edges to behaviors absent from a pipeline, an edge to an open behavior method's class in every pipeline it is part of, and a
///     behavior method's <c>[BehaviorOrder]</c> and <c>[RunsAfter]</c> overriding its class's.
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
        ("R8", ["MethodPhase", "Plain"]),
        ("R9", ["OverB", "OverA", "OverC"]),
    ];

    [Test]
    public async Task Pipelines_Run_In_Sort_Order(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
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
    public async Task The_Sorted_Pipelines_Compile_Without_Warnings(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + Source).Build().GenerateAsync(cancellationToken);

        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        await Assert.That(PipelineGeneratorTests.Source(result, HostEmitter.MediatorHintName)).Contains("global::Sample.Wrap.Handle<global::Sample.R7a, string, ");
    }

    private static readonly string Source = BuildSource();

    private static string BuildSource()
    {
        var builder = new StringBuilder(
            """
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
        builder.Append(Closed("EdgeA", "R3", "[RunsAfter(typeof(EdgeC))]\n[RunsAfter(typeof(EdgeB))]"));
        builder.Append(Closed("EdgeB", "R3", "[RunsBefore(typeof(EdgeC))]"));
        builder.Append(Closed("EdgeC", "R3"));
        builder.Append(Handler("R3", "EdgeA", "EdgeC", "EdgeB"));

        // R4: no position and no edges: ordinal class name.
        builder.Append(Closed("TieB", "R4"));
        builder.Append(Closed("TieA", "R4"));
        builder.Append(Handler("R4", "TieB", "TieA"));

        // R5: edges that agree with phase change nothing (name order alone would put AgreeInner first).
        builder.Append(Closed("AgreeInner", "R5", "[BehaviorOrder(Phase.Transaction)]\n[RunsAfter(typeof(AgreeOuter))]"));
        builder.Append(Closed("AgreeOuter", "R5", "[BehaviorOrder(Phase.Observability)]\n[RunsBefore(typeof(AgreeInner))]"));
        builder.Append(Handler("R5", "AgreeInner", "AgreeOuter"));

        // R6: an edge to a behavior that is not in this pipeline (TieA is R4's) is ignored.
        builder.Append(Closed("AbsentB", "R6", "[RunsBefore(typeof(TieA))]"));
        builder.Append(Closed("AbsentA", "R6", "[RunsAfter(typeof(TieB))]"));
        builder.Append(Handler("R6", "AbsentB", "AbsentA"));

        // R7: typeof names the class, so an edge to a class with an open behavior method holds in every pipeline it is part of.
        builder.Append(Open("Wrap"));
        builder.Append(Open("Inner", "[RunsAfter(typeof(Wrap))]"));
        builder.Append(Handler("R7a", "Inner", "Wrap"));
        builder.Append(Handler("R7b", "Inner", "Wrap"));

        // R8: a behavior method's [BehaviorOrder] overrides its class's for that kind.
        builder.Append(Closed("MethodPhase", "R8", "[BehaviorOrder(Phase.Transaction)]", "[BehaviorOrder(Phase.Observability)]"));
        builder.Append(Closed("Plain", "R8"));
        builder.Append(Handler("R8", "Plain", "MethodPhase"));

        // R9: a behavior method's [RunsAfter] list replaces its class's. Only the method's edge (OverB outside OverA) applies, so OverC,
        // after OverA by name, stays innermost; the class's edge alone, or both edges, would put OverC before OverA (OverB, OverC, OverA).
        builder.Append(Closed("OverA", "R9", "[RunsAfter(typeof(OverC))]", "[RunsAfter(typeof(OverB))]"));
        builder.Append(Closed("OverB", "R9"));
        builder.Append(Closed("OverC", "R9"));
        builder.Append(Handler("R9", "OverA", "OverB", "OverC"));
        return builder.ToString();
    }

    private static string Closed(string name, string request, string attributes = "", string methodAttributes = "") =>
        $$"""
          [RequestBehavior]
          {{attributes}}
          public static class {{name}}
          {
              {{methodAttributes}}
              public static ValueTask<string> Handle<TNext>({{request}} request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<string>
              {
                  Log.Write("{{name}}");
                  return next.InvokeAsync(cancellationToken);
              }
          }


          """;

    private static string Open(string name, string attributes = "") =>
        $$"""
          [RequestBehavior]
          {{attributes}}
          public static class {{name}}
          {
              public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                  where TRequest : IRequest<TResponse>
                  where TNext : struct, INext<TResponse>
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
