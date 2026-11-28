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

    /// <summary>
    ///     What a source with C# 15 <c>closed</c> types starts with: on .NET 10, whose reference assemblies the tests compile against there,
    ///     the <c>IsClosedTypeAttribute</c> the compiler writes on a closed type (CS0656 without it); on .NET 11, which has it, nothing. A
    ///     block namespace, so it can precede the types of the global namespace or another block namespace.
    /// </summary>
    public static string ClosedTypeAttribute { get; } =
        typeof(object).Assembly.GetType("System.Runtime.CompilerServices.IsClosedTypeAttribute") is null
            ? "namespace System.Runtime.CompilerServices { public sealed class IsClosedTypeAttribute : System.Attribute { } }\n"
            : "";

    /// <summary>One source the host generator generated, by hint name (<c>HostEmitter.MediatorHintName</c>, for example).</summary>
    public static string HostSource(GeneratorTestResults result, string hintName) =>
        result.Results[typeof(HostGenerator)].SyntaxTrees.Single(t => t.FilePath.EndsWith(hintName, StringComparison.Ordinal)).ToString();
}
