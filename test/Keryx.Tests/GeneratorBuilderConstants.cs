using Keryx.Analyzers;
using Microsoft.CodeAnalysis;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Keryx.Tests;

internal static class GeneratorBuilderConstants
{
    public static GeneratorTestContextBuilder Builder { get; } = GeneratorTestContextBuilder
                                                                .Create()
                                                                .WithGenerator<HandlerGenerator>()
                                                                .AddReferences(
                                                                     typeof(Unit).Assembly,
                                                                     typeof(Attribute).Assembly,
                                                                     typeof(Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute).Assembly
                                                                 )
                                                                // IServiceProvider, which handlers may inject, is forwarded through System.ComponentModel.
                                                                .AddReferences(
                                                                     MetadataReference.CreateFromFile(
                                                                         Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.ComponentModel.dll")
                                                                     )
                                                                 );
}
