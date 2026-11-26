using Keryx.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Keryx.Tests.CodeFixes;

/// <summary>
///     Runs an analyzer and a code fix through the source generator testing helpers, applies every offered code action, and snapshots the
///     source before and every changed or added document after each action. Each fixed solution is compiled with the handler generator and
///     analyzed again: the fixed diagnostic must be gone and the result must have no compiler errors. The diagnostics the analyzer still
///     reports are part of the snapshot.
/// </summary>
public abstract class CodeFixTest : GeneratorTest
{
    protected async Task<CodeFixSnapshot> FixAsync<TAnalyzer, TCodeFix>(
        string source,
        CancellationToken cancellationToken,
        params string[] additionalSources
    )
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {
        var result = await Builder
                          .WithLanguageVersion(LanguageVersion.Latest)
                          .WithAnalyzer<TAnalyzer>()
                          .WithCodeFix<TCodeFix>()
                          .AddSources([source, .. additionalSources])
                          .Build()
                          .GenerateAsync(cancellationToken);

        var actions = new List<CodeActionSnapshot>();
        foreach (var fix in result.CodeFixResults[typeof(TCodeFix)].ResolvedFixes.OrderBy(static f => f.Diagnostic.Location.SourceSpan.Start))
        {
            var original = fix.Document.Project.Solution;
            foreach (var action in fix.CodeActions)
            {
                var fixedMessage = fix.Diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
                var documents = new SortedDictionary<string, string>(StringComparer.Ordinal);
                var remaining = new List<string>();
                foreach (var operation in ( await action.CodeAction.GetOperationsAsync(cancellationToken) ).OfType<ApplyChangesOperation>())
                {
                    foreach (var change in operation.ChangedSolution.GetChanges(original).GetProjectChanges())
                    {
                        foreach (var id in change.GetChangedDocuments().Concat(change.GetAddedDocuments()))
                        {
                            var document = operation.ChangedSolution.GetDocument(id)!;
                            documents[document.Name] = ( await document.GetTextAsync(cancellationToken) ).ToString();
                        }

                        var diagnostics = await AnalyzeFixedProjectAsync<TAnalyzer>(change.NewProject, cancellationToken);
                        var title = action.CodeAction.Title;
                        await Assert.That(diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error && !d.Id.StartsWith("KRX", StringComparison.Ordinal)).Select(static d => d.ToString()))
                                    .IsEmpty()
                                    .Because($"'{title}' must leave code that compiles");
                        await Assert.That(diagnostics.Where(d => d.Id == fix.Diagnostic.Id && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture) == fixedMessage).Select(static d => d.ToString()))
                                    .IsEmpty()
                                    .Because($"'{title}' must fix '{fixedMessage}'");
                        remaining.AddRange(diagnostics.Where(static d => d.Id.StartsWith("KRX", StringComparison.Ordinal)).Select(static d => $"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}"));
                    }
                }

                remaining.Sort(StringComparer.Ordinal);
                actions.Add(
                    new(
                        $"{fix.Diagnostic.Id}: {fixedMessage}",
                        action.CodeAction.Title,
                        action.CodeAction.EquivalenceKey,
                        documents,
                        remaining
                    )
                );
            }
        }

        return new([source, .. additionalSources], actions);
    }

    /// <summary>
    ///     Compiles a fixed project with the handler generator (unless the workspace already ran it) and returns the compiler diagnostics and
    ///     what <typeparamref name="TAnalyzer" /> reports on the result.
    /// </summary>
    private static async Task<IReadOnlyList<Diagnostic>> AnalyzeFixedProjectAsync<TAnalyzer>(Project project, CancellationToken cancellationToken)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var compilation = ( await project.GetCompilationAsync(cancellationToken) )!;
        if (!compilation.SyntaxTrees.Any(static t => t.FilePath.EndsWith("KeryxManifest.g.cs", StringComparison.Ordinal)))
        {
            _ = CSharpGeneratorDriver
               .Create([new HandlerGenerator().AsSourceGenerator()], parseOptions: (CSharpParseOptions?)project.ParseOptions)
               .RunGeneratorsAndUpdateCompilation(compilation, out compilation, out _, cancellationToken);
        }

        var analyzerDiagnostics = await compilation.WithAnalyzers([new TAnalyzer()]).GetAnalyzerDiagnosticsAsync(cancellationToken);
        return [.. compilation.GetDiagnostics(cancellationToken), .. analyzerDiagnostics];
    }

    public sealed record CodeFixSnapshot(string[] Before, IReadOnlyList<CodeActionSnapshot> Actions);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="Remaining">The diagnostics <c>TAnalyzer</c> still reports after the action, sorted.</param>
    public sealed record CodeActionSnapshot(
        string Diagnostic,
        string Title,
        string? EquivalenceKey,
        IReadOnlyDictionary<string, string> After,
        IReadOnlyList<string> Remaining
    );
}
