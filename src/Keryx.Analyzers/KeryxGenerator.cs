using Microsoft.CodeAnalysis;

namespace Keryx.Analyzers;

/// <summary>
///     The Keryx source generator. A placeholder that generates nothing until handler discovery lands (ADR 0001, phase 2).
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class KeryxGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context) { }
}
