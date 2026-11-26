namespace Keryx.Analyzers;

/// <summary>
///     The tracking names of the generators' incremental steps (ADR 0001, Incremental generation rules: every step has one). The incremental
///     tests read the steps by these names.
/// </summary>
internal static class TrackingNames
{
    /// <summary>Whether the parse options are C# 11 or later (KRX022).</summary>
    public const string LanguageVersion = "Keryx.LanguageVersion";

    /// <summary>What the handler generator reads from the compilation itself. Fed directly by the compilation, so it always runs.</summary>
    public const string CompilationSettings = "Keryx.CompilationSettings";

    /// <summary><see cref="CompilationSettings" /> paired with <see cref="LanguageVersion" />, before they are merged.</summary>
    public const string HandlerSettingsInputs = "Keryx.HandlerSettingsInputs";

    /// <summary>The handler generator's settings: <see cref="LanguageVersion" /> combined with <see cref="CompilationSettings" />.</summary>
    public const string HandlerSettings = "Keryx.HandlerSettings";

    /// <summary>Every <c>Handle</c>/<c>HandleAsync</c> method projected to a handler model, or nothing.</summary>
    public const string HandlerCandidates = "Keryx.HandlerCandidates";

    /// <summary>The <see cref="HandlerCandidates" /> that produced a model (still typed as nullable).</summary>
    public const string HandlerModels = "Keryx.HandlerModels";

    /// <summary>The handler models.</summary>
    public const string Handlers = "Keryx.Handlers";

    /// <summary>The handler models combined with the settings, one per generated handler source.</summary>
    public const string HandlerSources = "Keryx.HandlerSources";
}
