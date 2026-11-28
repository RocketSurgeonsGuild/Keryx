using System.Reflection;
using Keryx.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Keryx.Tests.Abstractions;

public class KnownTypeNamesTests
{
    private static string[] KnownNames { get; } = [.. typeof(KnownTypeNames)
                                                  .GetFields(BindingFlags.Public | BindingFlags.Static)
                                                  .Where(static f => f.IsLiteral)
                                                  .Select(static f => (string)f.GetRawConstantValue()!)];

    [Test]
    public async Task Every_Known_Type_Name_Resolves_Against_Keryx()
    {
        var compilation = CSharpCompilation.Create(
            "KnownTypeNames",
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Unit).Assembly.Location),
            ]
        );

        await Assert.That(KnownNames).IsNotEmpty();
        foreach (var name in KnownNames)
        {
            var symbol = compilation.GetTypeByMetadataName(name);
            await Assert.That(symbol).IsNotNull().Because(name);
            await Assert.That(symbol!.ContainingAssembly.Name).IsEqualTo("Keryx").Because(name);
        }
    }

    [Test]
    public async Task Every_Public_Keryx_Type_Has_A_Known_Type_Name()
    {
        var exported = typeof(Unit).Assembly.GetExportedTypes().Select(static t => t.FullName!).Order(StringComparer.Ordinal).ToArray();

        await Assert.That(KnownNames.Order(StringComparer.Ordinal).ToArray()).IsEquivalentTo(exported);
    }

    [Test]
    public async Task Every_Well_Known_Type_Name_Resolves()
    {
        var names = typeof(WellKnownTypeNames).GetFields(BindingFlags.Public | BindingFlags.Static)
                                              .Where(static f => f.IsLiteral)
                                              .Select(static f => (string)f.GetRawConstantValue()!)
                                              .ToArray();
        var compilation = CSharpCompilation.Create(
            "WellKnownTypeNames",
            references:
            [
                .. TestReferences.Framework,
                MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute).Assembly.Location),
            ]
        );

        await Assert.That(names).IsNotEmpty();
        foreach (var name in names)
        {
            await Assert.That(compilation.GetTypeByMetadataName(name)).IsNotNull().Because(name);
        }
    }
}
