using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Keryx.Analyzers.CodeFixes;

/// <summary>
///     Code fixes for the Keryx diagnostics. A placeholder that fixes nothing until the KRX001 and KRX004 fixes land (ADR 0001, phase 2).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(KeryxCodeFixProvider))]
[Shared]
public sealed class KeryxCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override Task RegisterCodeFixesAsync(CodeFixContext context) => Task.CompletedTask;
}
