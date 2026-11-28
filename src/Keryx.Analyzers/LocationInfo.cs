using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Keryx.Analyzers;

/// <summary>
///     An equatable stand-in for a source <see cref="Location" />, so the host generator's cached models can say where to report a diagnostic
///     without holding a <see cref="Location" /> (ADR 0001, Diagnostics and code fixes).
/// </summary>
/// <param name="FilePath">The source file.</param>
/// <param name="Span">The span in the file's text.</param>
/// <param name="LineSpan">The same span as lines and columns.</param>
internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    /// <summary>The location as a <see cref="Location" /> again, for reporting.</summary>
    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

    /// <summary>
    ///     The location on its syntax tree when the compilation has it, so per-tree analyzer config (<c>.editorconfig</c> severity) applies to a
    ///     diagnostic reported there (OQ-35); otherwise the file location of <see cref="ToLocation()" />.
    /// </summary>
    /// <param name="trees">The compilation's syntax trees by file path.</param>
    public Location ToLocation(IReadOnlyDictionary<string, SyntaxTree> trees) =>
        trees.TryGetValue(FilePath, out var tree) && Span.End <= tree.Length ? Location.Create(tree, Span) : ToLocation();

    /// <summary>The equatable form of a source location, or <see langword="null" /> when the location is not in source.</summary>
    public static LocationInfo? From(Location? location) =>
        location is { IsInSource: true, SourceTree: { } tree }
            ? new(tree.FilePath, location.SourceSpan, location.GetLineSpan().Span)
            : null;
}
