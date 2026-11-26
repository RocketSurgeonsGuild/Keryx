using System.Collections.Immutable;
using Keryx.Analyzers.Behaviors;
using Keryx.Analyzers.Diagnostics;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     KRX008 as the host checks it (ADR 0001, Ordering; Diagnostics): for behavior pairs in its composed pipelines where at least one
///     behavior is referenced (declared in another assembly). Pairs of behaviors declared in the host's own source are the analyzer's. Each
///     pair is reported once, however many pipelines contain it.
/// </summary>
internal static class HostBehaviorDiagnostics
{
    public static IEnumerable<HostDiagnosticModel> Ordering(HostSettings settings, HostComposition composition)
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
                        if (Compare(outer, inner) <= 0 || !reported.Add($"{outer.Definition}|{inner.Definition}"))
                        {
                            continue;
                        }

                        yield return new(
                            Descriptors.InvalidBehaviorOrdering,
                            edge.Location ?? settings.HostAttribute,
                            new([Display(behavior.Identity), Contradiction(behavior, edge.Edge.RunsAfter, target)])
                        );
                    }
                }
            }
        }

        foreach (var cycle in composition.Cycles)
        {
            // Keyed on the behavior classes, so the same cycle in pipelines of other closings is reported once.
            if (CycleDiagnostic(settings, cycle) is { } diagnostic
             && reported.Add("cycle|" + string.Join("|", cycle.Select(static b => b.Definition).Order(StringComparer.Ordinal))))
            {
                yield return diagnostic;
            }
        }
    }

    /// <summary>
    ///     KRX008 for one cycle the sort found (a strongly connected component, in name order), unless all its members are in source (then the
    ///     analyzer reports it). Reported at the first in-source edge between two members of the cycle, else on <c>[assembly: KeryxHost]</c>.
    /// </summary>
    private static HostDiagnosticModel? CycleDiagnostic(HostSettings settings, ImmutableArray<PipelineBehaviorModel> cycle)
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
            location ?? settings.HostAttribute,
            new([Display(cycle[0].Identity), $"is part of a RunsAfter/RunsBefore cycle between {names}, so no order satisfies them"])
        );
    }

    /// <summary>Compares two behaviors by (phase, order), the position edges must agree with.</summary>
    private static int Compare(PipelineBehaviorModel left, PipelineBehaviorModel right)
    {
        var byPhase = left.Phase.CompareTo(right.Phase);
        return byPhase != 0 ? byPhase : left.Order.CompareTo(right.Order);
    }

    /// <summary>The KRX008 message for an edge that contradicts phase or order.</summary>
    internal static string Contradiction(PipelineBehaviorModel behavior, bool runsAfter, PipelineBehaviorModel target) =>
        BehaviorOrderingText.Contradiction(runsAfter, Display(target.Identity), (behavior.Phase, behavior.Order), (target.Phase, target.Order));

    private static string Display(string type) => type.Replace("global::", "");
}
