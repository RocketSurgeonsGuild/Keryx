using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Keryx.Tests.Diagnostics;

/// <summary>
///     Runs one analyzer, with the handler generator, over a source through the source generator testing helpers, and returns what it reports.
///     <c>host</c> sets <c>build_property.KeryxHost</c> (<see langword="true" />: a host, <see langword="false" />: a project that opts out);
///     without it the option is unset, which is not a host either.
/// </summary>
public abstract class AnalyzerTest : GeneratorTest
{
    protected const string Usings = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Keryx;
        using Microsoft.Extensions.DependencyInjection;

        """;

    protected async Task<ImmutableArray<Diagnostic>> AnalyzeAsync<TAnalyzer>(
        string source,
        CancellationToken cancellationToken,
        LanguageVersion languageVersion = LanguageVersion.Latest,
        string? projectName = null,
        bool? host = null
    )
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var builder = projectName is null ? Builder : Builder.WithProjectName(projectName);
        builder = host is { } isHost ? builder.WithKeryxHost(isHost) : builder;
        var result = await builder
                          .WithLanguageVersion(languageVersion)
                          .WithAnalyzer<TAnalyzer>()
                          .AddSources(Usings + source)
                          .Build()
                          .GenerateAsync(cancellationToken);
        return result.AnalyzerResults[typeof(TAnalyzer)].Diagnostics;
    }

    /// <summary>Analyzers run concurrently, so diagnostics are sorted by location, then ID, before a snapshot.</summary>
    protected static Diagnostic[] Sorted(IEnumerable<Diagnostic> diagnostics) =>
        [.. diagnostics.OrderBy(static d => d.Location.SourceSpan.Start).ThenBy(static d => d.Id, StringComparer.Ordinal).ThenBy(static d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal)];

    protected static string[] Ids(ImmutableArray<Diagnostic> diagnostics) => [.. diagnostics.Select(static d => d.Id).Order(StringComparer.Ordinal)];

    protected static string[] Messages(ImmutableArray<Diagnostic> diagnostics, string id) =>
        [.. diagnostics.Where(d => d.Id == id).Select(static d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal)];
}
