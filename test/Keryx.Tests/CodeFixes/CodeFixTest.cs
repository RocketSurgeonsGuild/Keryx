using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Keryx.Tests.CodeFixes;

/// <summary>
///     Runs an analyzer and a code fix through the source generator testing helpers, applies every offered code action, and snapshots the
///     source before and every changed or added document after each action (the library's per-action project changes). Each fixed project is
///     compiled again through the library with the handler generator and the analyzer: the fixed diagnostic must be gone and the result must
///     have no compiler errors. The Keryx diagnostics still reported (the analyzer's and the generators') are part of the snapshot. With
///     <c>host</c>, the project is a host (<c>build_property.KeryxHost</c> = <c>true</c>), for the fix and for the re-verification.
/// </summary>
public abstract class CodeFixTest : GeneratorTest
{
    protected async Task<CodeFixSnapshot> FixAsync<TAnalyzer, TCodeFix>(
        string source,
        CancellationToken cancellationToken,
        bool host = false,
        params string[] additionalSources
    )
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {
        var result = await ( host ? Builder.WithKeryxHost() : Builder )
                          .WithLanguageVersion(LanguageVersion.Latest)
                          .WithAnalyzer<TAnalyzer>()
                          .WithCodeFix<TCodeFix>()
                          .AddSources([source, .. additionalSources])
                          .Build()
                          .GenerateAsync(cancellationToken);

        var actions = new List<CodeActionSnapshot>();
        var fixedProjects = 0;
        foreach (var fix in result.CodeFixResults[typeof(TCodeFix)].ResolvedFixes.OrderBy(static f => f.Diagnostic.Location.SourceSpan.Start))
        {
            var fixedMessage = fix.Diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
            foreach (var action in fix.CodeActions)
            {
                var title = action.CodeAction.Title;
                var documents = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var id in action.Changes.GetChangedDocuments().Concat(action.Changes.GetAddedDocuments()))
                {
                    var document = action.Changes.NewProject.GetDocument(id)!;
                    documents[document.Name] = ( await document.GetTextAsync(cancellationToken) ).ToString();
                }

                var diagnostics = await AnalyzeFixedProjectAsync<TAnalyzer>(action.Changes.NewProject, $"Fixed{fixedProjects++}", host, cancellationToken);
                await Assert.That(diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error && !d.Id.StartsWith("KRX", StringComparison.Ordinal)).Select(static d => d.ToString()))
                            .IsEmpty()
                            .Because($"'{title}' must leave code that compiles");
                await Assert.That(diagnostics.Where(d => d.Id == fix.Diagnostic.Id && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture) == fixedMessage).Select(static d => d.ToString()))
                            .IsEmpty()
                            .Because($"'{title}' must fix '{fixedMessage}'");
                var remaining = diagnostics.Where(static d => d.Id.StartsWith("KRX", StringComparison.Ordinal))
                                           .Select(static d => $"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}")
                                           .Order(StringComparer.Ordinal)
                                           .ToList();
                actions.Add(new($"{fix.Diagnostic.Id}: {fixedMessage}", title, action.CodeAction.EquivalenceKey, documents, remaining));
            }
        }

        return new([source, .. additionalSources], actions);
    }

    /// <summary>
    ///     Compiles a fixed project's documents again through the testing library, with the handler generator and
    ///     <typeparamref name="TAnalyzer" />, and returns every diagnostic of the result: the compiler's, the generators' and the analyzer's.
    /// </summary>
    private async Task<IReadOnlyList<Diagnostic>> AnalyzeFixedProjectAsync<TAnalyzer>(Project project, string projectName, bool host, CancellationToken cancellationToken)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var builder = ( host ? Builder.WithKeryxHost() : Builder ).WithProjectName(projectName).WithLanguageVersion(LanguageVersion.Latest).WithAnalyzer<TAnalyzer>();
        foreach (var document in project.Documents)
        {
            builder = builder.AddSource(document.Name, ( await document.GetTextAsync(cancellationToken) ).ToString());
        }

        var result = await builder.Build().GenerateAsync(cancellationToken);
        return result.FinalDiagnostics;
    }

    public sealed record CodeFixSnapshot(string[] Before, IReadOnlyList<CodeActionSnapshot> Actions);

    /// <summary>One code action: the diagnostic it fixes, its title and key, the documents it changes or adds, and what is left.</summary>
    /// <param name="Remaining">The diagnostics <c>TAnalyzer</c> still reports after the action, sorted.</param>
    public sealed record CodeActionSnapshot(
        string Diagnostic,
        string Title,
        string? EquivalenceKey,
        IReadOnlyDictionary<string, string> After,
        IReadOnlyList<string> Remaining
    );
}
