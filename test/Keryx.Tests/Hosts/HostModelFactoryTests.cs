using Keryx.Analyzers.Hosts;

namespace Keryx.Tests.Hosts;

/// <summary>
///     The host namespace (ADR 0001, Opting in) and the KRX031 target framework decision (ADR 0001, Which projects may target what).
/// </summary>
public class HostModelFactoryTests
{
    [Test]
    [Arguments(null, "Assembly")]
    [Arguments("", "Assembly")]
    [Arguments("My.App", "My.App")]
    [Arguments("1st..My App+", "_1st._.My_App_")]
    [Arguments("Contoso.namespace", "Contoso.@namespace")]
    [Arguments("Über.Café", "Über.Café")]
    public async Task Namespace_Is_Sanitised(string? rootNamespace, string expected) =>
        await Assert.That(HostModelFactory.Namespace(rootNamespace, "Assembly")).IsEqualTo(expected);

    [Test]
    [Arguments(".NETCoreApp", "v10.0", null)]
    [Arguments(".NETCoreApp", "v11.0", null)]
    [Arguments(".NETCoreApp", "v9.0", ".NETCoreApp,Version=v9.0")]
    [Arguments(".NETStandard", "v2.0", ".NETStandard,Version=v2.0")]
    [Arguments(".NETFramework", "v4.8", ".NETFramework,Version=v4.8")]
    public async Task Target_Framework_Properties_Decide_KRX031(string identifier, string version, string? expected) =>
        await Assert.That(
                         HostModelFactory.UnsupportedTargetFramework(
                             new(true, null, identifier, version),
                             new(true, "Host", "System.Runtime", new(11, 0, 0, 0))
                         )
                     )
                    .IsEqualTo(expected);

    [Test]
    [Arguments("System.Runtime", "10.0.0.0", null)]
    [Arguments("System.Private.CoreLib", "11.0.0.0", null)]
    [Arguments("System.Runtime", "9.0.0.0", "a framework whose System.Runtime is version 9.0")]
    [Arguments("netstandard", "2.0.0.0", "a framework whose netstandard is version 2.0")]
    [Arguments("mscorlib", "4.0.0.0", "a framework whose mscorlib is version 4.0")]
    public async Task The_Runtime_Assembly_Decides_KRX031_Without_Properties(string assembly, string version, string? expected) =>
        await Assert.That(HostModelFactory.UnsupportedTargetFramework(new(true, null, null, null), new(true, "Host", assembly, Version.Parse(version))))
                    .IsEqualTo(expected);

    [Test]
    [Arguments(true, true, true, true)]
    [Arguments(false, true, true, false)]
    [Arguments(true, false, true, false)]
    [Arguments(true, true, false, false)]
    public async Task The_Host_Generator_Is_Enabled_Only_In_A_Host(bool isHost, bool referencesKeryx, bool languageVersionSupported, bool expected)
    {
        // A host is the KeryxHost build property (ADR 0001, Opting in), in a compilation that references Keryx, at C# 14 or later (KRX022).
        var settings = HostModelFactory.CreateSettings(
            new(referencesKeryx, "Host", "System.Runtime", new(10, 0, 0, 0)),
            new(isHost, null, null, null),
            languageVersionSupported
        );

        await Assert.That(settings.Enabled).IsEqualTo(expected);
    }
}
