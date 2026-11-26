using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Keryx.Benchmarks.BaselineBenchmarks).Assembly).Run(args);
