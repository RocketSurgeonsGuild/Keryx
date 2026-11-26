using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Keryx.Analyzers.Handlers;

/// <summary>
///     A rule violation found by <see cref="HandlerAnalysis" />. The analyzers report it; the handler generator only uses it to decide whether
///     to skip a handler (it never reports diagnostics).
/// </summary>
internal sealed class Issue(
    DiagnosticDescriptor descriptor,
    Location? location,
    bool outsideHostOnly,
    ImmutableDictionary<string, string?>? properties,
    params object?[] arguments
)
{
    public DiagnosticDescriptor Descriptor { get; } = descriptor;
    public Location? Location { get; } = location;

    /// <summary>Whether the rule applies only outside a host (the <c>public</c> part of KRX004, and KRX016).</summary>
    public bool OutsideHostOnly { get; } = outsideHostOnly;

    public ImmutableDictionary<string, string?> Properties { get; } = properties ?? ImmutableDictionary<string, string?>.Empty;
    public object?[] Arguments { get; } = arguments;

    public Diagnostic ToDiagnostic() => Diagnostic.Create(Descriptor, Location, Properties, Arguments);
}

/// <summary>
///     Collects the issues of one handler or type, and remembers whether any of them is an error, so the generator can skip what the analyzers
///     reject.
/// </summary>
internal sealed class IssueCollector
{
    private List<Issue>? _issues;

    public IReadOnlyList<Issue> Issues => _issues ?? (IReadOnlyList<Issue>)Array.Empty<Issue>();

    /// <summary>An error that applies everywhere.</summary>
    public bool HasErrors { get; private set; }

    /// <summary>An error that applies only outside a host.</summary>
    public bool HasOutsideHostErrors { get; private set; }

    public void Add(DiagnosticDescriptor descriptor, Location? location, params object?[] arguments) =>
        Add(new(descriptor, location, false, null, arguments));

    public void Add(Issue issue)
    {
        ( _issues ??= [] ).Add(issue);
        if (issue.Descriptor.DefaultSeverity != DiagnosticSeverity.Error)
        {
            return;
        }

        if (issue.OutsideHostOnly)
        {
            HasOutsideHostErrors = true;
        }
        else
        {
            HasErrors = true;
        }
    }

    /// <summary>
    ///     Marks the input invalid without a diagnostic of its own, because another rule reports the cause on another symbol (for example
    ///     KRX011 on a request type that a handler takes).
    /// </summary>
    public void Invalidate() => HasErrors = true;

    public void ReportTo(Action<Diagnostic> report, bool isHost)
    {
        foreach (var issue in Issues)
        {
            if (isHost && issue.OutsideHostOnly)
            {
                continue;
            }

            report(issue.ToDiagnostic());
        }
    }
}
