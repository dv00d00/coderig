using Rig.Cli.CommandLine;

namespace Rig.Cli.Live;

// The ROUTING POLICY for a one-shot query command: ask the resident index if one is watching this directory,
// otherwise read the store. Called from `reaches`/`path`/`callers`/`tree` and nowhere else.
//
// ON BY DEFAULT, and the reasoning is the whole point rather than a convenience. The reflex is to make this
// opt-in because "another process answered" sounds unsafe. That has it backwards: when a resident host is
// running, the STORE is the stale answer — it is pinned to whatever commit was last indexed, while the live
// index reflects the tree as it is now. The failure this program exists to remove is confidently answering
// about pre-edit code, so the honest default is live-when-available, with the source stated on every routed
// answer. `--no-live` (and RIG_NO_LIVE) force the store for anyone who wants the indexed snapshot.
//
// WHERE THE DISCLOSURE GOES, and why it is not stdout. Every routed answer carries the host's source line —
// which names the SOURCE and its staleness — on STDERR, ahead of the command's own stderr. stdout is left
// byte-identical to what the command produced, because `--format tsv` working "for free" is one of the two
// reasons this transport carries rendered text at all: a `live:` line prepended to TSV would break every awk
// consumer rig has. This also matches where rig already puts every other disclosure (ambiguity notices, seed
// resolution, the intrinsic hint, compile health) — stdout is the answer, stderr says what to trust about it.
//
// WHAT THE NO-HOST CASE PRINTS: nothing. The store path with no host running is byte-identical to rig before
// this slice, on BOTH streams, which is a hard requirement (and one the live/store parity gates enforce for
// us — LiveReachesTests and LivePathCallersTests compare store stderr against live stderr exactly). So the
// `live:` line is a POSITIVE marker: present => resident facts, absent => the store, and there is no third
// source it could have been. A host that was found and could NOT serve is the case that gets a sentence,
// because that one is a surprise.
internal static class LiveRoute
{
    // Session-wide opt-out, for a shell where the store snapshot is what you want for a while (comparing
    // against an indexed commit, reproducing a report). Same effect as passing --no-live to every query.
    internal const string DisableEnvironmentVariable = "RIG_NO_LIVE";

    // Returns the exit code when the resident host answered, or null to mean "run the store path" — which is
    // every case except a successful route, including every possible failure of the transport.
    internal static async Task<int?> TryAnswerAsync(string verb, object options, CommandIo io, bool noLive)
    {
        if (noLive || DisabledByEnvironment())
        {
            return null;
        }

        // `--store <ref>` NAMES a commit-scoped store. That is an explicit request for a specific indexed
        // snapshot, and the resident index is not one — routing it would answer a different question.
        if (io.WorkspaceLocation.StoreRef is not null)
        {
            return null;
        }

        var outcome = await LiveQueryClient.TryAskAsync(verb, options, io.WorkspaceLocation.WorkingDirectory);
        switch (outcome.Status)
        {
            case LiveRouteStatus.NoHost:
                return null;

            case LiveRouteStatus.Failed:
                // TWO facts, both required, because the store is a DIFFERENT snapshot of this tree than the
                // live index: WHY live declined, and WHICH source is about to answer instead. The second half
                // is completed by the store path itself — StoreAnswerDisclosure prints a `store: <id> @
                // <commit> — <freshness>` line as it opens the store, immediately after this one — so the
                // pointer here is what ties the two lines into one disclosure rather than leaving a reader to
                // guess that the answer below came from somewhere else entirely.
                io.TextOutput.Error.WriteLine(
                    $"live: a resident index is watching this directory but did not answer ({outcome.Reason}) — answering from the .rig store instead; "
                        + "the store is a separately indexed snapshot, and the `store:` line below names it and its freshness."
                );
                return null;

            default:
                var answer = outcome.Answer!;
                // Disclosure FIRST, so a reader sees the source before the command's own notes.
                io.TextOutput.Error.WriteLine(answer.Disclosure);
                io.TextOutput.Output.Write(answer.Out);
                io.TextOutput.Error.Write(answer.Err);
                return answer.Exit;
        }
    }

    private static bool DisabledByEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(DisableEnvironmentVariable);
        return value is not null
            && (
                string.Equals(value, "1", StringComparison.Ordinal)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
            );
    }
}
