using System.Text.RegularExpressions;

namespace Keryx.Tests.Behaviors;

/// <summary>
///     How a test source writes its <c>RunsAfter</c>/<c>RunsBefore</c> edges: every ordering test runs once per form and expects the same
///     result (ADR 0001, Ordering: <c>[RunsAfter&lt;X&gt;]</c> means exactly <c>[RunsAfter(typeof(X))]</c>).
/// </summary>
public enum OrderingForm
{
    /// <summary><c>[RunsAfter(typeof(X))]</c>.</summary>
    TypeOf,

    /// <summary><c>[RunsAfter&lt;X&gt;]</c>.</summary>
    Generic,

    /// <summary>The two forms alternating, so one class can carry both.</summary>
    Mixed,
}

/// <summary>Expands <c>{{After:X}}</c> and <c>{{Before:X}}</c> placeholders in a test source into attributes of one <see cref="OrderingForm" />.</summary>
public static partial class OrderingForms
{
    public static string Expand(string source, OrderingForm form)
    {
        var occurrence = 0;
        return Placeholder().Replace(
            source,
            match =>
            {
                var attribute = match.Groups["kind"].Value == "After" ? "RunsAfter" : "RunsBefore";
                var target = match.Groups["target"].Value;
                var generic = form switch
                {
                    OrderingForm.TypeOf => false,
                    OrderingForm.Generic => true,
                    OrderingForm.Mixed => occurrence++ % 2 == 1,
                    _ => throw new ArgumentOutOfRangeException(nameof(form), form, null),
                };
                return generic ? $"[{attribute}<{target}>]" : $"[{attribute}(typeof({target}))]";
            }
        );
    }

    [GeneratedRegex(@"\{\{(?<kind>After|Before):(?<target>[^}]+)\}\}")]
    private static partial Regex Placeholder();
}
