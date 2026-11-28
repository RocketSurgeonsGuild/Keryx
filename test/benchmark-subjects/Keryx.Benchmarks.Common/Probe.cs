using System.Runtime.CompilerServices;

namespace Keryx.Benchmarks.Common;

/// <summary>
///     Counts handler and behavior calls during <c>Keryx.Benchmarks --verify</c>, which checks that every subject really runs the
///     scenario's handlers and behaviors (ADR 0001, Benchmarks). In an ordinary build, <see cref="Enabled" /> is read once, from an
///     <see cref="AppContext" /> switch only the verify run sets (under the JIT and in the NativeAOT publish). The builds BenchmarkDotNet
///     makes (JIT and NativeAOT) compile the probe out (owner decision 34, open-questions OQ-73): the benchmark runner sets the MSBuild
///     property <c>KeryxBenchmarksProbe=false</c> in its own environment before BenchmarkDotNet builds, under which <see cref="Enabled" />
///     is a <c>const</c> <c>false</c> and <see cref="Hit" /> is empty, so no measured call reads a static field or branches on it. A
///     NativeAOT build could not fold the <c>static readonly</c> field: it has no tiers and cannot run this class's static constructor at
///     compile time.
/// </summary>
public static class Probe
{
    /// <summary>The <see cref="AppContext" /> switch that turns the probe on. Set it before anything touches <see cref="Probe" />.</summary>
    public const string SwitchName = "Keryx.Benchmarks.Probe";

    private static int _hits;

#if KERYX_BENCHMARKS_NO_PROBE
    /// <summary>Whether calls are counted: never in a build with <c>KeryxBenchmarksProbe=false</c> (the benchmark builds).</summary>
    public const bool Enabled = false;
#else
    /// <summary>Whether calls are counted (only in a verify run).</summary>
    public static readonly bool Enabled = AppContext.TryGetSwitch(SwitchName, out var enabled) && enabled;
#endif

    /// <summary>Records one handler or behavior call.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Hit()
    {
#if !KERYX_BENCHMARKS_NO_PROBE
        if (Enabled)
        {
            Interlocked.Increment(ref _hits);
        }
#endif
    }

    /// <summary>
    ///     Whether this build has no probe (built with <c>KeryxBenchmarksProbe=false</c>, as the builds BenchmarkDotNet makes are). Every
    ///     comparison benchmark's <c>[GlobalSetup]</c> fails unless it is true, so a benchmark run (the CI Dry jobs included, JIT and
    ///     NativeAOT) fails when the property does not reach BenchmarkDotNet's build. A method rather than a <c>const</c>, which would make
    ///     the caller's other branch unreachable (CS0162, an error here).
    /// </summary>
    public static bool IsCompiledOut()
    {
#if KERYX_BENCHMARKS_NO_PROBE
        return true;
#else
        return false;
#endif
    }

    /// <summary>Returns the calls recorded since the last call and starts counting again.</summary>
    public static int Take() => Interlocked.Exchange(ref _hits, 0);

    /// <summary>
    ///     Throws unless calls are being counted: the probe must be compiled in (no <c>KeryxBenchmarksProbe=false</c>) and its switch set.
    ///     A method rather than a read of <see cref="Enabled" /> at the call site, which would be a constant in a build without the probe.
    /// </summary>
    public static void EnsureCounting()
    {
#if KERYX_BENCHMARKS_NO_PROBE
        throw new InvalidOperationException(
            "This build has no verification probe (KeryxBenchmarksProbe=false); build without it to verify."
        );
#else
        if (!Enabled)
        {
            throw new InvalidOperationException($"Set the {SwitchName} AppContext switch before verifying.");
        }
#endif
    }
}
