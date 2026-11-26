using System.Collections.Immutable;
using Keryx.Analyzers.Behaviors;
using Keryx.Analyzers.Diagnostics;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     KRX008 as the host checks it (ADR 0001, Ordering; Diagnostics): for behavior pairs in its composed pipelines where at least one
///     behavior is referenced (declared in another assembly). Pairs of behaviors declared in the host's own source are the analyzer's. Each
///     pair of behavior classes is reported once, however many pipelines contain it, and once per kind where a behavior method overrides its
///     class's ordering.
/// </summary>
internal static class HostBehaviorDiagnostics
{
    public static IEnumerable<HostDiagnosticModel> Ordering(HostComposition composition)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pipeline in composition.BehaviorLists)
        {
            foreach (var behavior in pipeline)
            {
                foreach (var edge in behavior.Edges)
                {
                    foreach (var target in pipeline)
                    {
                        if (ReferenceEquals(target, behavior) || ( behavior.InSource && target.InSource ) || !edge.Edge.Matches(target.Key))
                        {
                            continue;
                        }

                        // Normalised to (outer, inner), so the pair is reported once whichever side declares the edge: RunsAfter X puts X
                        // outside, RunsBefore X puts X inside.
                        var (outer, inner) = edge.Edge.RunsAfter ? (target, behavior) : (behavior, target);
                        var kind = KindText(behavior, target);
                        if (Compare(outer, inner) <= 0 || !reported.Add($"{outer.Identity}|{inner.Identity}|{kind}"))
                        {
                            continue;
                        }

                        yield return new(
                            Descriptors.InvalidBehaviorOrdering,
                            edge.Location,
                            new([Display(behavior.Identity), Contradiction(behavior, edge.Edge.RunsAfter, target, kind)])
                        );
                    }
                }
            }
        }

        foreach (var cycle in composition.Cycles)
        {
            // Keyed on the behavior classes, so the same cycle in pipelines of other targets is reported once.
            if (CycleDiagnostic(cycle) is { } diagnostic
             && reported.Add("cycle|" + string.Join("|", cycle.Select(static b => b.Identity).Order(StringComparer.Ordinal)) + "|" + KindText([.. cycle])))
            {
                yield return diagnostic;
            }
        }
    }

    /// <summary>
    ///     KRX008 for one cycle the sort found (a strongly connected component, in name order), unless all its members are in source (then the
    ///     analyzer reports it). Reported at the first in-source edge between two members of the cycle, else with no location.
    /// </summary>
    private static HostDiagnosticModel? CycleDiagnostic(ImmutableArray<PipelineBehaviorModel> cycle)
    {
        if (cycle.All(static b => b.InSource))
        {
            return null;
        }

        LocationInfo? location = null;
        foreach (var behavior in cycle)
        {
            foreach (var edge in behavior.Edges)
            {
                if (location is null && edge.Location is { } declared && cycle.Any(other => !ReferenceEquals(other, behavior) && edge.Edge.Matches(other.Key)))
                {
                    location = declared;
                }
            }
        }

        var names = string.Join(", ", cycle.Select(static b => $"'{Display(b.Identity)}'"));
        return new(
            Descriptors.InvalidBehaviorOrdering,
            location,
            new([Display(cycle[0].Identity), BehaviorOrderingText.Cycle(names, KindText([.. cycle]))])
        );
    }

    /// <summary>Compares two behaviors by (phase, order), the position edges must agree with.</summary>
    private static int Compare(PipelineBehaviorModel left, PipelineBehaviorModel right)
    {
        var byPhase = left.Phase.CompareTo(right.Phase);
        return byPhase != 0 ? byPhase : left.Order.CompareTo(right.Order);
    }

    /// <summary>The KRX008 message for an edge that contradicts phase or order.</summary>
    internal static string Contradiction(PipelineBehaviorModel behavior, bool runsAfter, PipelineBehaviorModel target, string? kind) =>
        BehaviorOrderingText.Contradiction(runsAfter, Display(target.Identity), (behavior.Phase, behavior.Order), (target.Phase, target.Order), kind);

    /// <summary>
    ///     The kind a check is specific to, when a behavior method of one of the behaviors overrides its class's ordering; otherwise
    ///     <see langword="null" />, and the check holds for every kind the classes share.
    /// </summary>
    private static string? KindText(params PipelineBehaviorModel[] behaviors) =>
        behaviors.Any(static b => b.OrderOverridden) ? BehaviorShapes.KindName(behaviors[0].Kind) : null;

    private static string Display(string type) => type.Replace("global::", "");
}
