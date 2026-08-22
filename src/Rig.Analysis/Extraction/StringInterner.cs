using System.Collections.Concurrent;

namespace Rig.Analysis.Extraction;

// Canonicalizes the RETAINED fact strings so equal values share one instance. The fact tables are wide
// and hugely repetitive — measured on the MedDBase store (2.44M reference facts, 2026-08-22): the
// encoded EnclosingInvocations column alone is 585k retained strings totalling ~302M chars with only
// 106k distinct values (~58M chars), i.e. ~500 MB of duplicate UTF-16 in one column of one generation.
// Interning trades N copies for 1 copy + N references.
//
// Distinct from SymbolStringCache, deliberately: that cache is keyed by STRONG ISymbol references, so it
// must stay scoped to one extraction batch or it pins every Compilation it saw (see its header). This
// table is keyed by the string VALUE itself, so it can — and should — outlive the batch:
//   - one-shot `rig index`: one instance per run (created by the SolutionAnalyzer entry points), so the
//     227 per-project extraction batches share instances; the table dies with the run.
//   - resident (`rig watch`): ONE instance owned by the host for the process lifetime, shared by the
//     cold boot and every ResidentIndex re-extraction — so a reconcile GENERATION's strings alias the
//     base generation's instead of duplicating the whole retained string set per edit.
// The resident table only ever grows (a value edited away is never evicted); it is bounded by the union
// of values ever seen, and the entries are references to strings the facts mostly retain anyway, so the
// overhead is the dictionary itself (~50 bytes/distinct value), not a second copy of the data.
//
// Interning changes IDENTITY, never VALUE: Intern(s) returns a string Equals-equal to s, always. It
// therefore needs no schema bump and cannot change any derived output.
internal sealed class StringInterner
{
    private readonly ConcurrentDictionary<string, string> _table = new(StringComparer.Ordinal);

    // Kill switch for A/B measurement (and emergencies): RIG_NO_INTERN=1 makes every extraction path
    // run exactly as before this class existed, from ONE binary — which is what an interleaved
    // memory/time A/B needs (two builds would confound the comparison with build drift).
    private static readonly bool Disabled = Environment.GetEnvironmentVariable("RIG_NO_INTERN") == "1";

    public static StringInterner? CreateDefault() => Disabled ? null : new StringInterner();

    public string Intern(string value)
    {
        if (value.Length == 0)
        {
            return string.Empty;
        }

        // The key IS the value — no factory, no closure: first writer's instance wins, everyone
        // else gets a reference to it.
        return _table.GetOrAdd(value, value);
    }

    // Diagnostics for the measurement harnesses: how many distinct values the table canonicalizes.
    public int Count => _table.Count;
}
