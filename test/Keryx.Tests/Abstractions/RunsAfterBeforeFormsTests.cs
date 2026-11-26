using Keryx.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Keryx.Tests.Abstractions;

/// <summary>
///     <c>[RunsAfter(typeof(X))]</c> and <c>[RunsAfter&lt;X&gt;]</c> (and the same pair for <c>RunsBefore</c>) must mean the same edge. The
///     typeof form carries the behavior as a constructor argument; the generic form carries it as the attribute class's type argument. This
///     compiles both against the netstandard2.0 <c>Keryx.dll</c>, reads them back from metadata as a host does, and checks that the two
///     readings give the same edges.
/// </summary>
public class RunsAfterBeforeFormsTests
{
    private const string Source = """
        namespace Sample
        {
            public sealed class Audit { }
            public sealed class Log { }
            public sealed class Retry<T> { }

            [global::Keryx.RunsAfter(typeof(global::Sample.Audit))]
            [global::Keryx.RunsAfter(typeof(global::Sample.Retry<string>))]
            [global::Keryx.RunsBefore(typeof(global::Sample.Log))]
            public sealed class ByTypeOf { }

            [global::Keryx.RunsAfter<global::Sample.Audit>]
            [global::Keryx.RunsAfter<global::Sample.Retry<string>>]
            [global::Keryx.RunsBefore<global::Sample.Log>]
            public sealed class ByTypeArgument { }
        }
        """;

    [Test]
    [Arguments(LanguageVersion.CSharp11)]
    [Arguments(LanguageVersion.Latest)]
    public async Task Both_Forms_Produce_The_Same_Edges(LanguageVersion languageVersion)
    {
        var compilation = Compile(Source, languageVersion);
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);

        await Assert.That(emit.Diagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning).Select(static d => d.ToString()))
                    .IsEmpty();
        await Assert.That(emit.Success).IsTrue();

        var library = MetadataReference.CreateFromImage(image.ToArray());
        var host = CSharpCompilation.Create(
            "Host",
            references: [.. ManifestContractTests.FrameworkReferences, ManifestContractTests.KeryxNetStandard, library]
        );
        var assembly = (IAssemblySymbol)host.GetAssemblyOrModuleSymbol(library)!;
        var byTypeOf = assembly.GetTypeByMetadataName("Sample.ByTypeOf")!;
        var byTypeArgument = assembly.GetTypeByMetadataName("Sample.ByTypeArgument")!;

        // The two forms really are stored differently.
        await Assert.That(byTypeOf.GetAttributes().All(static a => a.AttributeClass!.Arity == 0 && a.ConstructorArguments.Length == 1)).IsTrue();
        await Assert.That(byTypeArgument.GetAttributes().All(static a => a.AttributeClass!.Arity == 1 && a.ConstructorArguments.IsEmpty)).IsTrue();

        var typeOfEdges = Edges(host, byTypeOf);
        var typeArgumentEdges = Edges(host, byTypeArgument);

        await Assert.That(typeOfEdges.Select(static e => e.Display))
                    .IsEquivalentTo(
                         ["RunsAfter Sample.Audit", "RunsAfter Sample.Retry<string>", "RunsBefore Sample.Log"],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
        await Assert.That(typeArgumentEdges.Length).IsEqualTo(typeOfEdges.Length);
        for (var i = 0; i < typeOfEdges.Length; i++)
        {
            await Assert.That(typeArgumentEdges[i].After).IsEqualTo(typeOfEdges[i].After);
            await Assert.That(SymbolEqualityComparer.Default.Equals(typeArgumentEdges[i].Target, typeOfEdges[i].Target)).IsTrue();
        }
    }

    [Test]
    public async Task Generic_Form_Needs_CSharp11()
    {
        var errors = Compile(Source, LanguageVersion.CSharp10).GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        // One "generic attributes are not available in C# 10" error per generic attribute usage; the typeof form compiles.
        await Assert.That(errors.Select(static d => d.Id)).IsEquivalentTo(Enumerable.Repeat("CS8936", 3));
    }

    [Test]
    public async Task Generic_Form_Cannot_Name_An_Open_Behavior()
    {
        const string open = """
            namespace Sample
            {
                public sealed class Retry<T> { }

                [global::Keryx.RunsAfter<global::Sample.Retry<>>]
                public sealed class Open { }
            }
            """;

        var errors = Compile(open, LanguageVersion.Latest).GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        // CS7003: unexpected use of an unbound generic name. Open behaviors use [RunsAfter(typeof(Retry<>))].
        await Assert.That(errors.Select(static d => d.Id)).Contains("CS7003");
    }

    /// <summary>
    ///     Reads the ordering edges of a behavior class the way the generator must: the typeof form from the constructor argument, the generic
    ///     form from the attribute class's type argument.
    /// </summary>
    private static (bool After, ITypeSymbol Target, string Display)[] Edges(Compilation compilation, INamedTypeSymbol type)
    {
        var runsAfter = compilation.GetTypeByMetadataName(KnownTypeNames.RunsAfterAttribute);
        var runsAfterOfT = compilation.GetTypeByMetadataName(KnownTypeNames.RunsAfterAttributeOfT);
        var runsBefore = compilation.GetTypeByMetadataName(KnownTypeNames.RunsBeforeAttribute);
        var runsBeforeOfT = compilation.GetTypeByMetadataName(KnownTypeNames.RunsBeforeAttributeOfT);

        return
        [
            .. type.GetAttributes()
                   .Select(attribute =>
                        {
                            var attributeClass = attribute.AttributeClass!;
                            var definition = attributeClass.OriginalDefinition;
                            var after = SymbolEqualityComparer.Default.Equals(definition, runsAfter)
                             || SymbolEqualityComparer.Default.Equals(definition, runsAfterOfT);
                            var generic = SymbolEqualityComparer.Default.Equals(definition, runsAfterOfT)
                             || SymbolEqualityComparer.Default.Equals(definition, runsBeforeOfT);
                            if (!after && !generic && !SymbolEqualityComparer.Default.Equals(definition, runsBefore))
                            {
                                throw new InvalidOperationException($"Unexpected attribute {attributeClass}.");
                            }

                            var target = generic ? attributeClass.TypeArguments[0] : (ITypeSymbol)attribute.ConstructorArguments[0].Value!;
                            return (after, target, $"{( after ? "RunsAfter" : "RunsBefore" )} {target.ToDisplayString()}");
                        }
                    ),
        ];
    }

    private static CSharpCompilation Compile(string source, LanguageVersion languageVersion) =>
        CSharpCompilation.Create(
            "Sample.Behaviors",
            [CSharpSyntaxTree.ParseText(source, new(languageVersion))],
            [.. ManifestContractTests.FrameworkReferences, ManifestContractTests.KeryxNetStandard],
            new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
        );
}
