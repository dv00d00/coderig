using System.Diagnostics;

namespace Rig.Cli;

// Opt-in per-phase timing for the query commands (`--time`). Prints "[time] <phase>: <ms>" to stderr —
// stderr (not stdout) so it never pollutes piped/--format output. Disabled (the default) it is a no-op:
// the Stopwatches are never allocated and every Lap/Total returns immediately, so it costs a null check.
internal sealed class PhaseTimer
{
    private readonly TextWriter? _writer;
    private readonly Stopwatch? _phase;
    private readonly Stopwatch? _total;

    public PhaseTimer(bool enabled, TextWriter writer)
    {
        if (!enabled)
        {
            return;
        }

        _writer = writer;
        _phase = Stopwatch.StartNew();
        _total = Stopwatch.StartNew();
    }

    public void Lap(string phase)
    {
        if (_writer is null)
        {
            return;
        }

        _writer.WriteLine($"[time] {phase}: {_phase!.ElapsedMilliseconds} ms");
        _phase.Restart();
    }

    // A phase whose SUB-steps were measured elsewhere — TraversalLoadTiming records them from inside the
    // graph load, which sits behind the fact-source seam this command cannot pass a timer through. Prints
    // the phase's own row exactly as Lap(phase) does, then one INDENTED row per sub-phase plus the
    // unattributed remainder, so the hierarchy reads. With no sub-phases (a source that records none, e.g.
    // the resident live host) it prints exactly the plain row.
    public void Lap(string phase, string remainderPhase, IReadOnlyList<(string Phase, TimeSpan Elapsed)>? subPhases)
    {
        if (_writer is null)
        {
            return;
        }

        var elapsed = _phase!.Elapsed;
        Lap(phase);
        if (subPhases is null || subPhases.Count == 0)
        {
            return;
        }

        var attributed = TimeSpan.Zero;
        foreach (var sub in subPhases)
        {
            _writer.WriteLine($"[time]   {sub.Phase}: {(long)sub.Elapsed.TotalMilliseconds} ms");
            attributed += sub.Elapsed;
        }

        _writer.WriteLine($"[time]   {remainderPhase}: {(long)Math.Max((elapsed - attributed).TotalMilliseconds, 0)} ms");
    }

    public void Total()
    {
        _writer?.WriteLine($"[time] total: {_total!.ElapsedMilliseconds} ms");
    }
}
