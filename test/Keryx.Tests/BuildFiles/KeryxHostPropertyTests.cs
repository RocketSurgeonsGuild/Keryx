using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Keryx.Tests.BuildFiles;

/// <summary>
///     The <c>KeryxHost</c> MSBuild property (ADR 0001, Opting in), evaluated by MSBuild over an SDK-style project that imports the package's
///     build files where NuGet does: the props from <c>obj/Consumer.csproj.nuget.g.props</c> (after <c>Directory.Build.props</c>, before the
///     project body) and the targets from <c>obj/Consumer.csproj.nuget.g.targets</c> (after the project body and after the .NET SDK has inferred
///     <c>TargetFrameworkIdentifier</c> and <c>TargetFrameworkVersion</c>). Executables, test projects and Android apps that target .NET 10 or
///     later are hosts (OQ-61, OQ-62), everything else is not, and a value the project sets wins, through <c>build/</c> and through
///     <c>buildTransitive/</c> alike. <c>build/Keryx.props</c> makes it compiler visible.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class KeryxHostPropertyTests
{
    private static string PackageDirectory { get; } =
        typeof(KeryxHostPropertyTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(static a => a.Key == "KeryxPackageDirectory").Value!;

    [Test]
    [Arguments("build", "<OutputType>Exe</OutputType>", "true")]
    [Arguments("build", "<OutputType>WinExe</OutputType>", "true")]
    [Arguments("build", "<OutputType>Library</OutputType>", "false")]
    [Arguments("build", "", "false")]
    [Arguments("build", "<IsTestProject>true</IsTestProject>", "true")]
    [Arguments("build", "<OutputType>Exe</OutputType><KeryxHost>false</KeryxHost>", "false")]
    [Arguments("build", "<IsTestProject>true</IsTestProject><KeryxHost>false</KeryxHost>", "false")]
    [Arguments("build", "<KeryxHost>true</KeryxHost>", "true")]
    [Arguments("buildTransitive", "<OutputType>Exe</OutputType>", "true")]
    [Arguments("buildTransitive", "", "false")]
    [Arguments("buildTransitive", "<KeryxHost>true</KeryxHost>", "true")]
    public async Task KeryxHost_Defaults_To_Executables_And_Test_Projects(string folder, string properties, string expected, CancellationToken cancellationToken)
    {
        using var project = new TestProject(folder, properties);

        await Assert.That(await project.KeryxHostAsync(cancellationToken)).IsEqualTo(expected);
    }

    /// <summary>
    ///     OQ-62: the default is <c>true</c> only where a host can run, .NET 10 or later, so a project below it that never asked to be a host
    ///     (an executable or test project of its own, or one that gets Keryx through a reference) does not fail with KRX031. A project that
    ///     sets <c>KeryxHost</c> itself still gets its value; below net10.0 that value is KRX031.
    /// </summary>
    [Test]
    [Arguments("build", "net8.0", "<OutputType>Exe</OutputType>", "false")]
    [Arguments("build", "net10.0", "<OutputType>Exe</OutputType>", "true")]
    [Arguments("build", "net11.0", "<OutputType>Exe</OutputType>", "true")]
    [Arguments("build", "net8.0", "<OutputType>WinExe</OutputType>", "false")]
    [Arguments("build", "net8.0", "<IsTestProject>true</IsTestProject>", "false")]
    [Arguments("build", "net48", "<OutputType>Exe</OutputType>", "false")]
    [Arguments("build", "net48", "<IsTestProject>true</IsTestProject>", "false")]
    [Arguments("build", "netstandard2.0", "<IsTestProject>true</IsTestProject>", "false")]
    [Arguments("build", "net8.0", "<OutputType>Exe</OutputType><KeryxHost>true</KeryxHost>", "true")]
    [Arguments("build", "net8.0", "<KeryxHost>true</KeryxHost>", "true")]
    [Arguments("build", "net10.0", "<OutputType>Exe</OutputType><KeryxHost>false</KeryxHost>", "false")]
    [Arguments("buildTransitive", "net8.0", "<OutputType>Exe</OutputType>", "false")]
    [Arguments("buildTransitive", "net10.0", "<OutputType>Exe</OutputType>", "true")]
    [Arguments("buildTransitive", "net8.0", "<OutputType>Exe</OutputType><KeryxHost>true</KeryxHost>", "true")]
    public async Task KeryxHost_Defaults_Only_Where_A_Host_Can_Run(
        string folder,
        string targetFramework,
        string properties,
        string expected,
        CancellationToken cancellationToken
    )
    {
        using var project = new TestProject(folder, properties, targetFramework: targetFramework);

        await Assert.That(await project.KeryxHostAsync(cancellationToken)).IsEqualTo(expected);
    }

    /// <summary>
    ///     OQ-61: the .NET for Android SDK rewrites an app's <c>OutputType</c> from <c>Exe</c> to <c>Library</c> and sets
    ///     <c>AndroidApplication</c> (Microsoft.Android.Sdk.DefaultProperties.targets, imported with the workload SDKs before the package's
    ///     targets), so an Android app is a host through <c>AndroidApplication</c>, on .NET 10 or later. The project sets both properties as the
    ///     Android SDK leaves them, so the rows evaluate the same with the Android workload installed or not; an Android library
    ///     (<c>AndroidApplication</c> false) is not a host.
    /// </summary>
    [Test]
    [Arguments("build", "net10.0-android", "<OutputType>Library</OutputType><AndroidApplication>true</AndroidApplication>", "true")]
    [Arguments("build", "net10.0-android", "<OutputType>Library</OutputType><AndroidApplication>false</AndroidApplication>", "false")]
    [Arguments("build", "net8.0-android", "<OutputType>Library</OutputType><AndroidApplication>true</AndroidApplication>", "false")]
    [Arguments("build", "net10.0", "<OutputType>Library</OutputType><AndroidApplication>true</AndroidApplication>", "true")]
    [Arguments("build", "net10.0-android", "<OutputType>Library</OutputType><AndroidApplication>true</AndroidApplication><KeryxHost>false</KeryxHost>", "false")]
    [Arguments("buildTransitive", "net10.0-android", "<OutputType>Library</OutputType><AndroidApplication>true</AndroidApplication>", "true")]
    public async Task KeryxHost_Defaults_To_Android_Apps(string folder, string targetFramework, string properties, string expected, CancellationToken cancellationToken)
    {
        using var project = new TestProject(folder, properties, targetFramework: targetFramework);

        await Assert.That(await project.KeryxHostAsync(cancellationToken)).IsEqualTo(expected);
    }

    /// <summary>
    ///     OQ-62: a test project that multi-targets <c>net8.0;net10.0</c> (a <c>netstandard2.0</c> Keryx library's tests) is a host only in its
    ///     net10.0 inner build. Each inner build is evaluated with its <c>TargetFramework</c> as a global property, as the outer build
    ///     dispatches it, and imports <c>build/</c>; the outer build (no target framework) imports <c>buildMultiTargeting/</c> and is not a
    ///     host, and evaluates without error although it has no target framework version to compare.
    /// </summary>
    [Test]
    [Arguments("build", "net8.0", "false")]
    [Arguments("build", "net10.0", "true")]
    [Arguments("build", "", "false")]
    [Arguments("buildTransitive", "net8.0", "false")]
    [Arguments("buildTransitive", "net10.0", "true")]
    [Arguments("buildTransitive", "", "false")]
    public async Task A_Multi_Targeting_Test_Project_Is_A_Host_Per_Inner_Build(
        string folder,
        string innerTargetFramework,
        string expected,
        CancellationToken cancellationToken
    )
    {
        using var project = new TestProject(folder, "<IsTestProject>true</IsTestProject>", targetFrameworks: "net8.0;net10.0");

        string[] innerBuild = innerTargetFramework is "" ? [] : [$"-p:TargetFramework={innerTargetFramework}"];
        await Assert.That(await project.KeryxHostAsync(cancellationToken, innerBuild)).IsEqualTo(expected);
    }

    [Test]
    public async Task A_Value_From_Directory_Build_Props_Wins(CancellationToken cancellationToken)
    {
        // Set before the package's props, as a repository-wide Directory.Build.props would.
        using var project = new TestProject("build", "<OutputType>Exe</OutputType>", "<PropertyGroup><KeryxHost>false</KeryxHost></PropertyGroup>");

        await Assert.That(await project.KeryxHostAsync(cancellationToken)).IsEqualTo("false");
    }

    [Test]
    [Arguments("build")]
    [Arguments("buildTransitive")]
    public async Task KeryxHost_Is_Compiler_Visible(string folder, CancellationToken cancellationToken)
    {
        using var project = new TestProject(folder, "<OutputType>Exe</OutputType>");

        var evaluated = await project.EvaluateAsync(cancellationToken, "-t:_KeryxAddCompilerVisibleProperties", "-getItem:CompilerVisibleProperty");

        var visible = evaluated.GetProperty("Items")
                               .GetProperty("CompilerVisibleProperty")
                               .EnumerateArray()
                               .Select(static item => item.GetProperty("Identity").GetString()!)
                               .ToArray();
        // Alongside the ones the .NET SDK's own analyzers ask for; each listed once (a property listed twice is written to the file twice).
        foreach (var property in (string[])["KeryxHost", "RootNamespace", "TargetFrameworkIdentifier", "TargetFrameworkVersion"])
        {
            await Assert.That(visible.Count(name => name == property)).IsEqualTo(1).Because(property);
        }
    }

    /// <summary>
    ///     A project in a temporary directory that imports the package's props and targets from a folder the way NuGet does: from the
    ///     <c>obj/&lt;project&gt;.nuget.g.props</c> and <c>.nuget.g.targets</c> files restore writes, which Microsoft.Common.props and
    ///     Microsoft.Common.CurrentVersion.targets import. The outer build of a multi-targeting project (no <c>TargetFramework</c>) imports
    ///     <c>buildMultiTargeting/</c> instead, as NuGet's generated files do.
    /// </summary>
    private sealed class TestProject : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "keryx-host-" + Guid.NewGuid().ToString("N"));

        public TestProject(
            string folder,
            string properties,
            string directoryBuildProps = "",
            string targetFramework = "net10.0",
            string? targetFrameworks = null
        )
        {
            var extensions = Path.Combine(_directory, "obj");
            Directory.CreateDirectory(extensions);
            File.WriteAllText(Path.Combine(_directory, "Directory.Build.props"), $"<Project>{directoryBuildProps}</Project>");
            File.WriteAllText(Path.Combine(extensions, "Consumer.csproj.nuget.g.props"), PackageImports(folder, "Keryx.props"));
            File.WriteAllText(Path.Combine(extensions, "Consumer.csproj.nuget.g.targets"), PackageImports(folder, "Keryx.targets"));
            var frameworks = targetFrameworks is null
                ? $"<TargetFramework>{targetFramework}</TargetFramework>"
                : $"<TargetFrameworks>{targetFrameworks}</TargetFrameworks>";
            File.WriteAllText(
                Path.Combine(_directory, "Consumer.csproj"),
                $"""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>{frameworks}{properties}</PropertyGroup></Project>"""
            );
        }

        private static string PackageImports(string folder, string file) =>
            $"""
             <Project>
               <ImportGroup Condition="'$(TargetFramework)' == ''"><Import Project="{Path.Combine(PackageDirectory, "buildMultiTargeting", file)}" /></ImportGroup>
               <ImportGroup Condition="'$(TargetFramework)' != ''"><Import Project="{Path.Combine(PackageDirectory, folder, file)}" /></ImportGroup>
             </Project>
             """;

        /// <summary>The evaluated <c>KeryxHost</c>.</summary>
        public async Task<string?> KeryxHostAsync(CancellationToken cancellationToken, params string[] switches)
        {
            // Two properties, so MSBuild prints JSON (one alone is printed as plain text).
            var evaluated = await EvaluateAsync(cancellationToken, ["-getProperty:KeryxHost", "-getProperty:TargetFramework", .. switches]);
            return evaluated.GetProperty("Properties").GetProperty("KeryxHost").GetString();
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temporary directory does not fail the test.
            }
        }

        /// <summary>
        ///     Runs <c>dotnet msbuild</c> over the project with the given switches and returns its JSON output. A run that fails without
        ///     printing anything (seen once in ~15 runs of this class, when 80 evaluations started right after a solution build; an
        ///     evaluation error always prints) is retried once.
        /// </summary>
        public async Task<JsonElement> EvaluateAsync(CancellationToken cancellationToken, params string[] switches)
        {
            for (var attempt = 1; ; attempt++)
            {
                var (exitCode, output, error) = await RunMSBuildAsync(switches, cancellationToken);
                if (exitCode == 0)
                {
                    return JsonElement.Parse(output);
                }

                if (attempt == 1 && string.IsNullOrWhiteSpace(output) && string.IsNullOrWhiteSpace(error))
                {
                    continue;
                }

                throw new InvalidOperationException($"dotnet msbuild exited with {exitCode}:\n{output}\n{error}");
            }
        }

        private async Task<(int ExitCode, string Output, string Error)> RunMSBuildAsync(string[] switches, CancellationToken cancellationToken)
        {
            // DOTNET_HOST_PATH is the dotnet that runs the tests (set by the .NET SDK); otherwise the one on the PATH.
            var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = _directory,
            };
            foreach (var argument in (string[])["msbuild", Path.Combine(_directory, "Consumer.csproj"), "-nologo", .. switches])
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode, await output, await error);
        }
    }
}
