using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Keryx.Tests;

/// <summary>
///     The MSBuild properties a real build passes the generators and analyzers as <c>build_property.*</c> options (the package's
///     <c>build/Keryx.props</c> and <c>build/Keryx.targets</c>), for the tests to set the same way.
/// </summary>
internal static class BuildProperties
{
    /// <summary>The option <c>build/Keryx.targets</c> sets from the <c>KeryxHost</c> MSBuild property (ADR 0001, Opting in).</summary>
    public const string KeryxHost = "build_property.KeryxHost";

    /// <summary>
    ///     Makes the compilation a host (<c>build_property.KeryxHost</c> = <c>true</c>), as <c>build/Keryx.targets</c> does for an executable,
    ///     a test project or a project that sets <c>&lt;KeryxHost&gt;true&lt;/KeryxHost&gt;</c>; with <see langword="false" />, a project that
    ///     sets it to <c>false</c>. Without either, the option is unset, which is not a host.
    /// </summary>
    public static GeneratorTestContextBuilder WithKeryxHost(this GeneratorTestContextBuilder builder, bool isHost = true) =>
        builder.AddGlobalOption(KeryxHost, isHost ? "true" : "false");

    /// <summary>
    ///     An options provider with the given global <c>build_property.*</c> options, for the tests that drive a generator or an analyzer with
    ///     Roslyn directly.
    /// </summary>
    public static AnalyzerConfigOptionsProvider Provider(params (string Key, string Value)[] globalOptions) => new OptionsProvider(globalOptions);

    /// <summary>The options of a host: <c>build_property.KeryxHost</c> = <c>true</c>.</summary>
    public static AnalyzerConfigOptionsProvider Host { get; } = Provider((KeryxHost, "true"));

    private sealed class OptionsProvider((string Key, string Value)[] globalOptions) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(globalOptions.ToDictionary(static o => o.Key, static o => o.Value, StringComparer.Ordinal));

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Options.Empty;
    }

    private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public static Options Empty { get; } = new([]);

        // The base method's out parameter is [NotNullWhen(true)]; that attribute is ambiguous here (the analyzers' Polyfill copy is visible
        // through InternalsVisibleTo), so the override leaves it off.
#pragma warning disable CS8765
        public override bool TryGetValue(string key, out string? value) => values.TryGetValue(key, out value);
#pragma warning restore CS8765
    }
}
