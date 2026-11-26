using System.Collections.Immutable;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;

namespace Keryx.Analyzers.Behaviors;

/// <summary>
///     One <c>RunsAfter</c>/<c>RunsBefore</c> edge of a behavior (ADR 0001, Ordering).
/// </summary>
/// <param name="RunsAfter"><see langword="true" /> for <c>RunsAfter</c> (the declaring behavior is inside the target), <see langword="false" /> for <c>RunsBefore</c>.</param>
/// <param name="Target">The behavior class the edge names.</param>
/// <param name="Attribute">The attribute that declared it, for locations.</param>
internal readonly record struct BehaviorEdge(bool RunsAfter, INamedTypeSymbol Target, AttributeData Attribute);

/// <summary>
///     Reads behavior positions (<c>[BehaviorOrder]</c>, <c>[RunsAfter]</c>, <c>[RunsBefore]</c>) and sorts pipelines (ADR 0001, Ordering). The
///     attributes go on the behavior class and apply to every behavior method it declares; the same attribute on a behavior method overrides
///     the class's for that method's kind.
/// </summary>
internal static class BehaviorOrdering
{
    /// <summary>The phase of a behavior without <c>[BehaviorOrder]</c>: <c>Phase.Default</c>.</summary>
    public const int DefaultPhase = 400;

    /// <summary>
    ///     The <c>[BehaviorOrder]</c> position of a behavior method: its own, else its class's; (<see cref="DefaultPhase" />, 0) without one.
    /// </summary>
    public static (int Phase, int Order) ReadPosition(IMethodSymbol method, KeryxSymbols symbols) =>
        ReadPosition(method.GetAttributes(), symbols) ?? ReadPosition(method.ContainingType.GetAttributes(), symbols) ?? (DefaultPhase, 0);

    private static (int Phase, int Order)? ReadPosition(ImmutableArray<AttributeData> attributes, KeryxSymbols symbols)
    {
        foreach (var attribute in attributes)
        {
            if (!KeryxSymbols.IsAttribute(attribute, symbols.BehaviorOrderAttribute))
            {
                continue;
            }

            var phase = attribute.ConstructorArguments is [{ Value: int value }] ? value : DefaultPhase;
            var order = 0;
            foreach (var named in attribute.NamedArguments)
            {
                if (named is { Key: "Order", Value.Value: int number })
                {
                    order = number;
                }
            }

            return (phase, order);
        }

        return null;
    }

    /// <summary>
    ///     The <c>RunsAfter</c>/<c>RunsBefore</c> edges of a behavior method: its own <c>[RunsAfter]</c> list when it has one, else its class's,
    ///     and likewise for <c>[RunsBefore]</c>; the method's edges first, then the class's, each in declaration order.
    /// </summary>
    public static ImmutableArray<BehaviorEdge> ReadEdges(IMethodSymbol method, KeryxSymbols symbols)
    {
        var afterOnMethod = Declares(method, runsAfter: true, symbols);
        var beforeOnMethod = Declares(method, runsAfter: false, symbols);
        var edges = ImmutableArray.CreateBuilder<BehaviorEdge>();
        AddEdges(edges, method.GetAttributes(), afterOnMethod, beforeOnMethod, symbols);
        AddEdges(edges, method.ContainingType.GetAttributes(), !afterOnMethod, !beforeOnMethod, symbols);
        return edges.ToImmutable();
    }

    /// <summary>
    ///     Whether a behavior method declares any ordering attribute of its own, so its ordering may differ from its class's (KRX008 is then
    ///     checked for its kind on its own).
    /// </summary>
    public static bool OverridesOrdering(IMethodSymbol method, KeryxSymbols symbols) =>
        method.GetAttributes()
              .Any(a => KeryxSymbols.IsAttribute(a, symbols.BehaviorOrderAttribute)
                     || KeryxSymbols.IsAttribute(a, symbols.RunsAfterAttribute)
                     || KeryxSymbols.IsAttribute(a, symbols.RunsBeforeAttribute));

    private static bool Declares(IMethodSymbol method, bool runsAfter, KeryxSymbols symbols) =>
        method.GetAttributes().Any(a => KeryxSymbols.IsAttribute(a, runsAfter ? symbols.RunsAfterAttribute : symbols.RunsBeforeAttribute));

    private static void AddEdges(ImmutableArray<BehaviorEdge>.Builder edges, ImmutableArray<AttributeData> attributes, bool after, bool before, KeryxSymbols symbols)
    {
        foreach (var attribute in attributes)
        {
            var runsAfter = KeryxSymbols.IsAttribute(attribute, symbols.RunsAfterAttribute);
            if (( runsAfter ? after : before && KeryxSymbols.IsAttribute(attribute, symbols.RunsBeforeAttribute) )
             && attribute.ConstructorArguments is [{ Kind: TypedConstantKind.Type, Value: INamedTypeSymbol { TypeKind: not TypeKind.Error } target }])
            {
                edges.Add(new(runsAfter, target, attribute));
            }
        }
    }

    /// <summary>The <c>typeof</c> operand a behavior class, an edge target or an exclusion compares by.</summary>
    public static string Operand(ITypeSymbol type) => CSharpText.TypeOf(type);

    /// <summary>
    ///     Sorts one pipeline's behaviors (ADR 0001, Ordering, Sort rules): phase ascending, then order ascending, then the
    ///     <c>RunsAfter</c>/<c>RunsBefore</c> edges inside each (phase, order) bucket topologically, then the ordinal fully qualified class
    ///     name. Edges across buckets do not move anything (they agree with phase and order, or are KRX008). Behaviors left unplaced by a
    ///     cycle keep name order; each cycle (a strongly connected component of two or more of them, in name order) is added to
    ///     <paramref name="cycles" />.
    /// </summary>
    public static ImmutableArray<T> Sort<T>(IEnumerable<T> behaviors, Func<T, OrderingKey> key, List<ImmutableArray<T>>? cycles = null)
    {
        var result = ImmutableArray.CreateBuilder<T>();
        var buckets = behaviors
                     .Select(b => (Behavior: b, Key: key(b)))
                     .GroupBy(static b => (b.Key.Phase, b.Key.Order))
                     .OrderBy(static g => g.Key.Phase)
                     .ThenBy(static g => g.Key.Order);

        foreach (var bucket in buckets)
        {
            var nodes = new List<(T Behavior, OrderingKey Key)>(bucket);
            nodes.Sort(static (left, right) => string.CompareOrdinal(left.Key.Identity, right.Key.Identity));
            var incoming = new int[nodes.Count];
            var outgoing = new List<int>[nodes.Count];
            for (var i = 0; i < nodes.Count; i++)
            {
                outgoing[i] = [];
            }

            for (var i = 0; i < nodes.Count; i++)
            {
                foreach (var edge in nodes[i].Key.Edges)
                {
                    for (var j = 0; j < nodes.Count; j++)
                    {
                        if (i == j || !edge.Matches(nodes[j].Key))
                        {
                            continue;
                        }

                        // RunsAfter: the target runs first (outside). RunsBefore: this one runs first.
                        var (from, to) = edge.RunsAfter ? (j, i) : (i, j);
                        if (!outgoing[from].Contains(to))
                        {
                            outgoing[from].Add(to);
                            incoming[to]++;
                        }
                    }
                }
            }

            var placed = new bool[nodes.Count];
            for (var count = 0; count < nodes.Count; count++)
            {
                // The ready node with the smallest name: nodes are in name order, so the first one found.
                var next = -1;
                for (var i = 0; i < nodes.Count; i++)
                {
                    if (!placed[i] && incoming[i] == 0)
                    {
                        next = i;
                        break;
                    }
                }

                if (next < 0)
                {
                    // A cycle: what is left keeps name order. Only the cycles themselves (strongly connected components of two or more of the
                    // nodes left) go to KRX008, not the nodes that wait on a cycle without being part of it.
                    for (var i = 0; i < nodes.Count; i++)
                    {
                        if (!placed[i])
                        {
                            result.Add(nodes[i].Behavior);
                        }
                    }

                    if (cycles is not null)
                    {
                        foreach (var component in StronglyConnectedComponents(nodes.Count, node => placed[node] ? [] : outgoing[node].Where(to => !placed[to])))
                        {
                            if (component.Count > 1)
                            {
                                component.Sort();
                                var members = ImmutableArray.CreateBuilder<T>(component.Count);
                                foreach (var member in component)
                                {
                                    members.Add(nodes[member].Behavior);
                                }

                                cycles.Add(members.MoveToImmutable());
                            }
                        }
                    }

                    break;
                }

                placed[next] = true;
                result.Add(nodes[next].Behavior);
                foreach (var to in outgoing[next])
                {
                    incoming[to]--;
                }
            }
        }

        return result.ToImmutable();
    }

    /// <summary>
    ///     The strongly connected components of a graph of <paramref name="count" /> nodes (Tarjan's algorithm), visiting roots in index
    ///     order, so the result is deterministic. Shared by the analyzer's and the host's KRX008 cycle checks.
    /// </summary>
    public static List<List<int>> StronglyConnectedComponents(int count, Func<int, IEnumerable<int>> successors)
    {
        var indexOf = new int[count];
        var lowLink = new int[count];
        var onStack = new bool[count];
        var stack = new Stack<int>();
        var components = new List<List<int>>();
        var next = 0;
        for (var i = 0; i < count; i++)
        {
            indexOf[i] = -1;
        }

        for (var root = 0; root < count; root++)
        {
            if (indexOf[root] < 0)
            {
                Visit(root);
            }
        }

        return components;

        void Visit(int node)
        {
            indexOf[node] = lowLink[node] = next++;
            stack.Push(node);
            onStack[node] = true;
            foreach (var to in successors(node))
            {
                if (indexOf[to] < 0)
                {
                    Visit(to);
                    lowLink[node] = Math.Min(lowLink[node], lowLink[to]);
                }
                else if (onStack[to])
                {
                    lowLink[node] = Math.Min(lowLink[node], indexOf[to]);
                }
            }

            if (lowLink[node] != indexOf[node])
            {
                return;
            }

            var component = new List<int>();
            int member;
            do
            {
                member = stack.Pop();
                onStack[member] = false;
                component.Add(member);
            }
            while (member != node);

            components.Add(component);
        }
    }
}

/// <summary>What the sort needs from one behavior in a pipeline.</summary>
/// <param name="Identity">The behavior class's <c>typeof</c> operand, which is also its ordinal sort name.</param>
/// <param name="Phase">Its phase.</param>
/// <param name="Order">Its order inside the phase.</param>
/// <param name="Edges">Its <c>RunsAfter</c>/<c>RunsBefore</c> edges.</param>
internal readonly record struct OrderingKey(string Identity, int Phase, int Order, IReadOnlyList<OrderingEdge> Edges);

/// <summary>An edge as the sort compares it.</summary>
/// <param name="RunsAfter">The edge's direction.</param>
/// <param name="Target">The target class's <c>typeof</c> operand.</param>
internal readonly record struct OrderingEdge(bool RunsAfter, string Target)
{
    /// <summary>Whether the edge names this behavior's class.</summary>
    public bool Matches(OrderingKey behavior) => string.Equals(Target, behavior.Identity, StringComparison.Ordinal);
}
