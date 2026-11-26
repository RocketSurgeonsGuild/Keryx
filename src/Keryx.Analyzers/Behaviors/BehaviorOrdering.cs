using System.Collections.Immutable;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;

namespace Keryx.Analyzers.Behaviors;

/// <summary>
///     One <c>RunsAfter</c>/<c>RunsBefore</c> edge declared on a behavior class, whichever form declared it (ADR 0001, Ordering).
/// </summary>
/// <param name="RunsAfter"><see langword="true" /> for <c>RunsAfter</c> (the declaring behavior is inside the target), <see langword="false" /> for <c>RunsBefore</c>.</param>
/// <param name="Target">The behavior the edge names: an unbound generic (<c>typeof(X&lt;,&gt;)</c>) matches every closing, any other type only itself.</param>
/// <param name="Attribute">The attribute that declared it, for locations.</param>
internal readonly record struct BehaviorEdge(bool RunsAfter, ITypeSymbol Target, AttributeData Attribute)
{
    /// <summary>Whether the edge names an open behavior, matching every closing of it.</summary>
    public bool TargetIsOpen => Target is INamedTypeSymbol { IsUnboundGenericType: true };
}

/// <summary>
///     Reads behavior positions (<c>[BehaviorOrder]</c>, <c>[RunsAfter]</c>, <c>[RunsBefore]</c>) and sorts pipelines (ADR 0001, Ordering).
/// </summary>
internal static class BehaviorOrdering
{
    /// <summary>The phase of a behavior without <c>[BehaviorOrder]</c>: <c>Phase.Default</c>.</summary>
    public const int DefaultPhase = 400;

    /// <summary>The <c>[BehaviorOrder]</c> position of a behavior class: phase, then order; (<see cref="DefaultPhase" />, 0) without one.</summary>
    public static (int Phase, int Order) ReadPosition(INamedTypeSymbol behavior, KeryxSymbols symbols)
    {
        foreach (var attribute in behavior.OriginalDefinition.GetAttributes())
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

        return (DefaultPhase, 0);
    }

    /// <summary>
    ///     The <c>RunsAfter</c>/<c>RunsBefore</c> edges a behavior class declares, in both forms, in declaration order. This is the one place
    ///     that reads them: the <c>typeof</c> form's constructor argument and the generic form's type argument give the same
    ///     <see cref="BehaviorEdge" />, so nothing after this sees which form was written.
    /// </summary>
    public static ImmutableArray<BehaviorEdge> ReadEdges(INamedTypeSymbol behavior, KeryxSymbols symbols)
    {
        var edges = ImmutableArray.CreateBuilder<BehaviorEdge>();
        foreach (var attribute in behavior.OriginalDefinition.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeClass)
            {
                continue;
            }

            var definition = attributeClass.OriginalDefinition;
            bool runsAfter;
            if (Is(definition, symbols.RunsAfterAttribute) || Is(definition, symbols.RunsAfterAttributeOfT))
            {
                runsAfter = true;
            }
            else if (Is(definition, symbols.RunsBeforeAttribute) || Is(definition, symbols.RunsBeforeAttributeOfT))
            {
                runsAfter = false;
            }
            else
            {
                continue;
            }

            // Generic form: [RunsAfter<X>] carries X as the attribute class's type argument. typeof form: [RunsAfter(typeof(X))] carries it
            // as the constructor argument.
            var target = attributeClass.IsGenericType
                ? attributeClass.TypeArguments[0]
                : attribute.ConstructorArguments is [{ Kind: TypedConstantKind.Type, Value: ITypeSymbol type }]
                    ? type
                    : null;
            if (target is null or { TypeKind: TypeKind.Error })
            {
                continue;
            }

            edges.Add(new(runsAfter, target, attribute));
        }

        return edges.ToImmutable();

        static bool Is(INamedTypeSymbol definition, INamedTypeSymbol? expected) =>
            expected is not null && SymbolEqualityComparer.Default.Equals(definition, expected);
    }

    /// <summary>
    ///     The <c>typeof</c> operand an edge or exclusion compares against: the open form (<c>X&lt;,&gt;</c>) for an unbound generic, else the
    ///     closed type.
    /// </summary>
    public static string Operand(ITypeSymbol type) => CSharpText.TypeOf(type);

    /// <summary>The <c>typeof</c> operand of a behavior class's definition: <c>X&lt;,&gt;</c> for a generic one, the type itself otherwise.</summary>
    public static string DefinitionOperand(INamedTypeSymbol behavior) =>
        behavior.IsGenericType ? CSharpText.TypeOf(behavior.ConstructUnboundGenericType()) : CSharpText.TypeOf(behavior);

    /// <summary>
    ///     Sorts one pipeline's behaviors (ADR 0001, Ordering, Sort rules): phase ascending, then order ascending, then the
    ///     <c>RunsAfter</c>/<c>RunsBefore</c> edges inside each (phase, order) bucket topologically, then the ordinal fully qualified type
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
/// <param name="Identity">The closed behavior's <c>typeof</c> operand, which is also its ordinal sort name.</param>
/// <param name="Definition">The <c>typeof</c> operand of its definition (<c>X&lt;,&gt;</c> for an open behavior).</param>
/// <param name="Phase">Its phase.</param>
/// <param name="Order">Its order inside the phase.</param>
/// <param name="Edges">Its <c>RunsAfter</c>/<c>RunsBefore</c> edges.</param>
internal readonly record struct OrderingKey(string Identity, string Definition, int Phase, int Order, IReadOnlyList<OrderingEdge> Edges);

/// <summary>An edge as the sort compares it.</summary>
/// <param name="RunsAfter">The edge's direction.</param>
/// <param name="Target">The target's <c>typeof</c> operand.</param>
/// <param name="TargetIsOpen">Whether the target is open, matching every closing (compared with <see cref="OrderingKey.Definition" />).</param>
internal readonly record struct OrderingEdge(bool RunsAfter, string Target, bool TargetIsOpen)
{
    /// <summary>
    ///     Whether the edge names this behavior: an open <c>typeof(X&lt;,&gt;)</c> matches every closing of <c>X</c>; a closed one, in either
    ///     form, matches only that closing.
    /// </summary>
    public bool Matches(OrderingKey behavior) =>
        TargetIsOpen ? string.Equals(Target, behavior.Definition, StringComparison.Ordinal) : string.Equals(Target, behavior.Identity, StringComparison.Ordinal);
}
