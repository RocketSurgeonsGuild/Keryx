using Keryx.Analyzers;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Keryx.Tests;

internal static class GeneratorBuilderConstants
{
    /// <summary>The references every test compilation has: the framework, Keryx, <c>System.ComponentModel</c> and the DI abstractions.</summary>
    public static GeneratorTestContextBuilder References { get; } = GeneratorTestContextBuilder
                                                                   .Create()
                                                                   .AddReferences(
                                                                        typeof(Unit).Assembly,
                                                                        typeof(Attribute).Assembly,
                                                                        typeof(Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute).Assembly
                                                                    )
                                                                   .AddReferences(TestReferences.ComponentModel);

    /// <summary><see cref="References" /> with the handler generator, as every project that references the Keryx package compiles.</summary>
    public static GeneratorTestContextBuilder Builder { get; } = References.WithGenerator<HandlerGenerator>();
}
