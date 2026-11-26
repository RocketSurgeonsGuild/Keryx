using System.Runtime.CompilerServices;

namespace Keryx.Benchmarks.Common;

/// <summary>
///     Counts handler and behavior calls during <c>Keryx.Benchmarks --verify</c>, which checks that every subject really runs the scenario's
///     handlers and behaviors (ADR 0001, Benchmarks). <see cref="Enabled" /> is read once, from an <see cref="AppContext" /> switch only the
///     verify run sets, and is a <c>static readonly</c> field: once the class is initialized the JIT treats it as a constant, so in a
///     benchmark run <see cref="Hit" /> compiles to nothing.
/// </summary>
public static class Probe
{
    /// <summary>The <see cref="AppContext" /> switch that turns the probe on. Set it before anything touches <see cref="Probe" />.</summary>
    public const string SwitchName = "Keryx.Benchmarks.Probe";

    private static int _hits;

    /// <summary>Whether calls are counted (only in a verify run).</summary>
    public static readonly bool Enabled = AppContext.TryGetSwitch(SwitchName, out var enabled) && enabled;

    /// <summary>Records one handler or behavior call.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Hit()
    {
        if (Enabled)
        {
            Interlocked.Increment(ref _hits);
        }
    }

    /// <summary>Returns the calls recorded since the last call and starts counting again.</summary>
    public static int Take() => Interlocked.Exchange(ref _hits, 0);
}
