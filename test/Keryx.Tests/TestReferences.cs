using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Keryx.Tests;

/// <summary>The metadata references the tests compile against, shared so every test names them the same way.</summary>
internal static class TestReferences
{
    private static string RuntimeDirectory { get; } = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

    /// <summary>The core framework assemblies alone (<c>System.Private.CoreLib</c>, <c>System.Runtime</c>, <c>netstandard</c>).</summary>
    public static MetadataReference[] Framework { get; } =
        [.. new[] { "System.Private.CoreLib.dll", "System.Runtime.dll", "netstandard.dll" }.Select(static name => MetadataReference.CreateFromFile(Path.Combine(RuntimeDirectory, name)))];

    /// <summary><c>System.ComponentModel</c>, which <see cref="IServiceProvider" /> (handlers may inject it) is forwarded through.</summary>
    public static MetadataReference ComponentModel { get; } = MetadataReference.CreateFromFile(Path.Combine(RuntimeDirectory, "System.ComponentModel.dll"));

    /// <summary>
    ///     The netstandard2.0 <c>Keryx.dll</c> that a contracts or handler library compiles against; its path reaches the tests through
    ///     <see cref="AssemblyMetadataAttribute" /> (see <c>Keryx.Tests.csproj</c>).
    /// </summary>
    public static MetadataReference KeryxNetStandard { get; } = MetadataReference.CreateFromFile(
        typeof(TestReferences).Assembly
                              .GetCustomAttributes<AssemblyMetadataAttribute>()
                              .Single(static a => a.Key == "KeryxNetStandardAssembly")
                              .Value!
    );
}
