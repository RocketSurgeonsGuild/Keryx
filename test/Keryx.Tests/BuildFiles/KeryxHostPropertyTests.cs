using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Keryx.Tests.BuildFiles;

/// <summary>
///     The <c>KeryxHost</c> MSBuild property (ADR 0001, Opting in), evaluated by MSBuild over an SDK-style project that imports the package's
///     build files the way NuGet does: the props from <c>Directory.Build.props</c>, before the project body, and the targets from
///     <c>Directory.Build.targets</c>, after it (NuGet imports them from <c>nuget.g.props</c> and <c>nuget.g.targets</c>). Executables and test
///     projects are hosts, everything else is not, and a value the project sets wins, through <c>build/</c> and through
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

        var evaluated = await project.EvaluateAsync(cancellationToken, "-getProperty:KeryxHost", "-getProperty:OutputType");

        await Assert.That(evaluated.GetProperty("Properties").GetProperty("KeryxHost").GetString()).IsEqualTo(expected);
    }

    [Test]
    public async Task A_Value_From_Directory_Build_Props_Wins(CancellationToken cancellationToken)
    {
        // Set before the package's props, as a repository-wide Directory.Build.props would.
        using var project = new TestProject("build", "<OutputType>Exe</OutputType>", "<PropertyGroup><KeryxHost>false</KeryxHost></PropertyGroup>");

        var evaluated = await project.EvaluateAsync(cancellationToken, "-getProperty:KeryxHost", "-getProperty:OutputType");

        await Assert.That(evaluated.GetProperty("Properties").GetProperty("KeryxHost").GetString()).IsEqualTo("false");
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

    /// <summary>A project in a temporary directory whose Directory.Build.props/targets import the package's props and targets from a folder.</summary>
    private sealed class TestProject : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "keryx-host-" + Guid.NewGuid().ToString("N"));

        public TestProject(string folder, string properties, string directoryBuildProps = "")
        {
            Directory.CreateDirectory(_directory);
            var build = Path.Combine(PackageDirectory, folder);
            File.WriteAllText(
                Path.Combine(_directory, "Directory.Build.props"),
                $"""<Project>{directoryBuildProps}<Import Project="{Path.Combine(build, "Keryx.props")}" /></Project>"""
            );
            File.WriteAllText(Path.Combine(_directory, "Directory.Build.targets"), $"""<Project><Import Project="{Path.Combine(build, "Keryx.targets")}" /></Project>""");
            File.WriteAllText(
                Path.Combine(_directory, "Consumer.csproj"),
                $"""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>{properties}</PropertyGroup></Project>"""
            );
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

        /// <summary>Runs <c>dotnet msbuild</c> over the project with the given switches and returns its JSON output.</summary>
        public async Task<JsonElement> EvaluateAsync(CancellationToken cancellationToken, params string[] switches)
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
            return process.ExitCode == 0
                ? JsonElement.Parse(await output)
                : throw new InvalidOperationException($"dotnet msbuild exited with {process.ExitCode}:\n{await output}\n{await error}");
        }
    }
}
