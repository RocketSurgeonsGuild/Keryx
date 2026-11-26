using BenchmarkDotNet.Running;
using Keryx.Benchmarks.Prototype;

// --prototype-disasm: run the Phase 4 gate pipelines to Tier 1 and exit, for DOTNET_JitDisasm on platforms where BenchmarkDotNet's
// DisassemblyDiagnoser is not available, for example:
//   DOTNET_JitDisasm='*Link*:InvokeAsync *Behavior*:Handle *Send*' DOTNET_JitStdOutFile=disasm.txt dotnet run -c Release -f net10.0 -- --prototype-disasm
if (args is ["--prototype-disasm"])
{
    Console.WriteLine(PipelinePrototypeBenchmarks.RunForDisassembly());
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Keryx.Benchmarks.BaselineBenchmarks).Assembly).Run(args);
