using Keryx.Analyzers;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Keryx.Tests;

internal static class GeneratorBuilderConstants
{
    public static GeneratorTestContextBuilder Builder { get; } = GeneratorTestContextBuilder
                                                                .Create()
                                                                .WithGenerator<KeryxGenerator>()
                                                                .AddReferences(
                                                                     typeof(Unit).Assembly,
                                                                     typeof(Attribute).Assembly
                                                                 );
}
