using BenchmarkDotNet.Running;
using Keryx.Benchmarks.Comparison;
using Keryx.Benchmarks.Prototype;

// --verify: run every comparison benchmark once, counting handler and behavior calls, and check them (Comparison/Verification.cs). The
// probe's switch is set before anything reads it.
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

// A benchmark that fails to build or throws fails the run (CI runs every benchmark with the Dry job).
var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return summaries.Any(static summary => summary.HasCriticalValidationErrors || summary.Reports.Any(static report => !report.Success)) ? 1 : 0;
