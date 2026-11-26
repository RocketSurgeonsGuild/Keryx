using Microsoft.CodeAnalysis.Diagnostics;

namespace Keryx.Analyzers;

/// <summary>
///     The MSBuild properties Keryx reads as <c>build_property.*</c> analyzer config options. The package's <c>build/Keryx.props</c> makes
///     them compiler visible; <c>build/Keryx.targets</c> sets <c>KeryxHost</c> (ADR 0001, Opting in).
/// </summary>
internal static class KeryxBuildProperties
{
    /// <summary>The MSBuild property that makes a project a host.</summary>
    public const string KeryxHost = "KeryxHost";

    /// <summary>
    ///     Whether the project is a host: its <c>KeryxHost</c> property is <c>true</c> (case-insensitive). Unset, empty or anything else is not
    ///     a host, so a compilation without the Keryx build files (a hand-wired analyzer) never is one.
    /// </summary>
    public static bool IsHost(AnalyzerConfigOptions globalOptions) =>
        Get(globalOptions, KeryxHost) is { } value && bool.TryParse(value, out var isHost) && isHost;

    /// <summary>The trimmed value of <c>build_property.<paramref name="name" /></c>, or <see langword="null" /> when it is absent or empty.</summary>
    public static string? Get(AnalyzerConfigOptions globalOptions, string name) =>
        globalOptions.TryGetValue("build_property." + name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
