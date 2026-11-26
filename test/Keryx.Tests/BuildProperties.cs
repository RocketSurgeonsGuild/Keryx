using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Keryx.Tests;

/// <summary>
///     The MSBuild properties a real build passes the generators and analyzers as <c>build_property.*</c> options (the package's
///     <c>build/Keryx.props</c> and <c>build/Keryx.targets</c>, and the SDK), set the same way in every test.
/// </summary>
internal static class BuildProperties
{
    /// <summary>The option <c>build/Keryx.targets</c> sets from the <c>KeryxHost</c> MSBuild property (ADR 0001, Opting in).</summary>
    public const string KeryxHost = "build_property.KeryxHost";

    /// <summary>The project's <c>RootNamespace</c>, which names the namespace of the generated host types.</summary>
    public const string RootNamespace = "build_property.RootNamespace";

    /// <summary>The project's <c>IntermediateOutputPath</c> (<c>obj/</c>), where a host writes its scan cache.</summary>
    public const string IntermediateOutputPath = "build_property.IntermediateOutputPath";

    /// <summary>
    ///     Makes the compilation a host (<c>build_property.KeryxHost</c> = <c>true</c>), as <c>build/Keryx.targets</c> does for an executable,
    ///     a test project or a project that sets <c>&lt;KeryxHost&gt;true&lt;/KeryxHost&gt;</c>; with <see langword="false" />, a project that
    ///     sets it to <c>false</c>. Without either, the option is unset, which is not a host.
    /// </summary>
    public static GeneratorTestContextBuilder WithKeryxHost(this GeneratorTestContextBuilder builder, bool isHost = true) =>
        builder.AddGlobalOption(KeryxHost, isHost ? "true" : "false");

    /// <summary>Sets the project's <c>RootNamespace</c>.</summary>
    public static GeneratorTestContextBuilder WithRootNamespace(this GeneratorTestContextBuilder builder, string rootNamespace) =>
        builder.AddGlobalOption(RootNamespace, rootNamespace);

    /// <summary>
    ///     Sets the project's target framework as the SDK passes it (<c>TargetFrameworkIdentifier</c>, <c>TargetFrameworkVersion</c>), for
    ///     KRX031.
    /// </summary>
    public static GeneratorTestContextBuilder WithTargetFramework(this GeneratorTestContextBuilder builder, string identifier, string version) =>
        builder.AddGlobalOption("build_property.TargetFrameworkIdentifier", identifier).AddGlobalOption("build_property.TargetFrameworkVersion", version);

    /// <summary>Sets the project's <c>IntermediateOutputPath</c> (absolute, ending with a directory separator, as MSBuild passes it).</summary>
    public static GeneratorTestContextBuilder WithIntermediateOutputPath(this GeneratorTestContextBuilder builder, string intermediateOutputPath) =>
        builder.AddGlobalOption(IntermediateOutputPath, intermediateOutputPath);
}
