using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Keryx.Benchmarks.Comparison;
using Keryx.Benchmarks.Prototype;

// --verify: run every comparison benchmark twice, counting handler and behavior calls, and check them (Comparison/Verification.cs). The
// probe's switch is set before anything reads it. The same switch runs the check in the NativeAOT build:
//   dotnet publish -c Release -f net10.0 --use-current-runtime -p:KeryxBenchmarksNativeAot=true -o aot && aot/Keryx.Benchmarks --verify
if (args is ["--verify"])
{
    AppContext.SetSwitch(Keryx.Benchmarks.Common.Probe.SwitchName, true);
    return await Verification.RunAsync();
}

// --prototype-disasm: run the Phase 4 gate pipelines to Tier 1 and exit, for DOTNET_JitDisasm on platforms where BenchmarkDotNet's
// DisassemblyDiagnoser is not available, for example:
//   DOTNET_JitDisasm='*Link*:InvokeAsync *Behavior*:Handle *Send*' DOTNET_JitStdOutFile=disasm.txt dotnet run -c Release -f net10.0 -- --prototype-disasm
if (args is ["--prototype-disasm"])
{
    Console.WriteLine(PipelinePrototypeBenchmarks.RunForDisassembly());
    return 0;
}

// A benchmark that fails to build or throws fails the run (CI runs every benchmark with the Dry job, with the JIT and with NativeAOT). The
// config leaves the subjects that are n/a under NativeAOT, and the JIT-only Phase 4 prototype, out of NativeAOT jobs (--runtimes
// nativeaot10.0; Comparison/NativeAot.cs) and fails the run when a comparison scenario is missing from --verify's list.
//
// The builds BenchmarkDotNet makes (JIT and NativeAOT) inherit this process's environment, where an MSBuild property can be set: with
// KeryxBenchmarksProbe=false, Probe.Enabled is a const false and Probe.Hit is empty, so no measured call carries the verification probe
// (owner decision 34). BenchmarkDotNet 0.15.8 passes ArtifactsPath and OutDir to those builds, so every project they rebuild goes to the
// generated project's folder and never over this process's own binaries. --verify, above, never sets the property.
//
// The NativeAOT job of PublishTo1.Baseline, a call that returns default, runs a fixed invocation count: BenchmarkDotNet 0.15.8's pilot
// measures it as 0 ns and throws (owner decision 39, OQ-77; Comparison/FixedInvocationCount.cs). Every other job and benchmark keeps its
// settings.
Environment.SetEnvironmentVariable("KeryxBenchmarksProbe", "false");
var config = DefaultConfig
            .Instance.AddFilter(NativeAot.Filter)
            .AddValidator(ScenarioListValidator.Instance)
            .WithOrderer(FixedInvocationCount.Instance);
var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
return summaries.Any(static summary => summary.HasCriticalValidationErrors || summary.Reports.Any(static report => !report.Success)) ? 1 : 0;
