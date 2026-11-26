using System.Globalization;

namespace Keryx.Analyzers.Behaviors;

/// <summary>The KRX008 message parts, shared by the analyzer and the host so both say the same thing.</summary>
internal static class BehaviorOrderingText
{
    private static readonly (int Value, string Name)[] Phases =
    [
        (0, "Outermost"),
        (100, "Observability"),
        (200, "Security"),
        (300, "Validation"),
        (400, "Default"),
        (450, "Caching"),
        (500, "Transaction"),
        (600, "Innermost"),
    ];

    /// <summary>
    ///     "runs after 'X' ([RunsAfter]), but its position (Phase.Default, order 0) puts it before 'X' (Phase.Validation, order 0); RunsAfter
    ///     and RunsBefore only order behaviors with the same phase and order".
    /// </summary>
    public static string Contradiction(bool runsAfter, string target, (int Phase, int Order) own, (int Phase, int Order) other) =>
        $"declares {( runsAfter ? "RunsAfter" : "RunsBefore" )} '{target}', but its position ({Position(own)}) puts it {( runsAfter ? "before (outside)" : "after (inside)" )} "
      + $"'{target}' ({Position(other)}); RunsAfter and RunsBefore only order behaviors that have the same phase and order";

    public static string Position((int Phase, int Order) position) =>
        $"{PhaseName(position.Phase)}, order {position.Order.ToString(CultureInfo.InvariantCulture)}";

    private static string PhaseName(int phase)
    {
        foreach (var (value, name) in Phases)
        {
            if (value == phase)
            {
                return "Phase." + name;
            }
        }

        return $"(Phase){phase.ToString(CultureInfo.InvariantCulture)}";
    }
}
