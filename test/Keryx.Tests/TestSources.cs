using Keryx.Analyzers;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Keryx.Tests;

/// <summary>Source text the tests share: what test sources start with, and how a generated host source is read back.</summary>
internal static class TestSources
{
    /// <summary>The usings (and <c>#nullable enable</c>) a test source starts with.</summary>
    public const string Usings = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Keryx;
        using Microsoft.Extensions.DependencyInjection;

        """;

    /// <summary>One source the host generator generated, by hint name (<c>HostEmitter.MediatorHintName</c>, for example).</summary>
    public static string HostSource(GeneratorTestResults result, string hintName) =>
        result.Results[typeof(HostGenerator)].SyntaxTrees.Single(t => t.FilePath.EndsWith(hintName, StringComparison.Ordinal)).ToString();
}
