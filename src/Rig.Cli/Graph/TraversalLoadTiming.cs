using System.Diagnostics;
using System.Threading;

namespace Rig.Cli.Graph;

// F3: `--time` SUB-PHASE attribution for the traversal graph load. Ambient rather than threaded, because the
// load sits behind IQueryFactSource and the query services, so the command that owns the PhaseTimer has no
// way to hand a timer down without plumbing one through every fact source. A command opens a scope around
// the span it wants attributed and reads Laps back after; TraversalGraphLoader records into whatever scope
// is open. Ambient invocation-scope precedent: StoreAnswerDisclosure.
//
// Costs nothing when timing is off: Begin returns null, IsActive is false, so the loader never allocates a
// Stopwatch and every Lap returns on a null check. Laps are appended from the ORCHESTRATING flow only,
// after the awaited sub-step has completed, so the appends are sequential and need no locking (same
// reasoning as PhaseTimings).
internal sealed class TraversalLoadTiming : IDisposable
{
    private static readonly AsyncLocal<TraversalLoadTiming?> Ambient = new();

    private readonly TraversalLoadTiming? _previous;
    private readonly List<(string Phase, TimeSpan Elapsed)> _laps = [];

    private TraversalLoadTiming(TraversalLoadTiming? previous) => _previous = previous;

    // The sub-phases recorded since the scope opened, in completion order.
    internal IReadOnlyList<(string Phase, TimeSpan Elapsed)> Laps => _laps;

    internal static bool IsActive => Ambient.Value is not null;

    // Null when timing is off — nothing is allocated and the loader stays on its no-op path. `using var` on
    // the nullable result is safe: a null resource is simply not disposed.
    internal static TraversalLoadTiming? Begin(bool enabled)
    {
        if (!enabled)
        {
            return null;
        }

        var scope = new TraversalLoadTiming(Ambient.Value);
        Ambient.Value = scope;
        return scope;
    }

    // Record `watch`'s elapsed as `phase` and restart it for the next sub-step — PhaseTimer.Lap's semantics
    // minus the printing (the command's PhaseTimer renders these nested under its own row). `watch` is null
    // whenever the loader ran with no scope open, which is the whole of the disabled path.
    internal static void Lap(string phase, Stopwatch? watch)
    {
        if (watch is null || Ambient.Value is not { } scope)
        {
            return;
        }

        scope._laps.Add((phase, watch.Elapsed));
        watch.Restart();
    }

    public void Dispose() => Ambient.Value = _previous;
}
