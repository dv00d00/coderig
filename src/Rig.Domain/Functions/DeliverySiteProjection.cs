using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// The pure CORE of publish→consumer DELIVERY-site projection — the rule-driven logic that turns raw
// projected fact rows (event reads + candidate arg-rule invocations) into the uniform DeliverySite list
// the framework-BLIND join (FactPathFinder.AddDeliveryEdges, baked into call_edges at graph build)
// consumes. Replaces the former per-framework EventDeliverySites + ActorDeliverySites pair: a codebase
// declares each mechanism in DATA (the `deliveryRules` rule section), and this projection is generic over it.
// The actor case is no longer inferred from the `actor:*` effect rules; both events and actors are pure rule
// data, each composing the two identity primitives ("symbol" / "path") this projection implements.
//
// It consumes each fact source ONCE regardless of how many rules use it:
//   - event-symbol rules (Producer.Source == "event-symbol"): event-read refs (RefKind=read, target "E:").
//     The event's `E:` DocID is the channel IdentityToken — an EXACT binding. Every `someEvent += H` AND
//     every raise (`someEvent?.Invoke()`) reads the event, so the role is ByColocation; the join decides
//     subscription (a co-located method-group ⇒ that handler) vs raise (none ⇒ producer). One site per read
//     per such rule (normally one event rule).
//   - arg rules (Producer.Source == "arg"): invocation refs whose (declaringType, method) match a rule's
//     Registration or Producer endpoint Methods×DeclaringTypes. The channel identity is the argument the
//     rule's `resolve` selects, both GATED to a member path (Contains('.'): a bare-variable name like
//     `tell(pid, …)` is not a stable cross-method identity and collides spuriously, so it is skipped):
//     "path" keeps the full member path; "leaf" keeps the LAST segment (bridging parallel registries that
//     share a leaf but differ by class prefix — e.g. tell `ProcessDns.X` ↔ spawn `ProcessNames.X`). An
//     invocation matching a Registration endpoint gets Role=Registration; a Producer endpoint, Role=
//     Producer. The process-name string identity is ~heuristic (more so for leaf).
//
// ArgumentIndex > 0 is not yet supported by the facts (only FirstArgumentName/arg0 is captured), so a
// "path" rule with ArgumentIndex != 0 falls back to arg0 — see AddArgEndpoint's note.
//
// Extracted so the STORE loader (Reads.LoadDeliverySitesAsync, which supplies the rows by SQL scan) and the
// in-memory twin (LiveReads.DeliverySites, which supplies them straight off an AnalysisResult) share ONE
// copy of this ~200-line logic instead of duplicating it. LiveFactSourceParityTests asserts the two agree.
public static class DeliverySiteProjection
{
    // One projected event-read reference fact: a `read` ref whose target is an event ("E:" DocID). The
    // caller filters (RefKind == read && target starts with "E:" && EnclosingSymbolId != null) and projects
    // these four columns; everything downstream of that is this projection's job.
    public sealed record EventRead(string? EnclosingSymbolId, string FilePath, int Line, string TargetSymbolId);

    // One projected CANDIDATE arg-rule invocation: an `invocation` ref with a captured first-argument name
    // whose target is a method DocID. That is the COARSE filter (such calls are few, so the unrefined set is
    // small); this projection refines it by the declaring-type+method gate.
    public sealed record ArgInvocation(string? EnclosingSymbolId, string FilePath, int Line, string? FirstArgumentName, string TargetSymbolId);

    // The rules whose producer endpoint reads event symbols — non-empty means the caller must supply event
    // reads. Exposed so a store-backed caller can skip the event-read SQL scan entirely when it is empty.
    public static IReadOnlyList<DeliveryRule> EventRules(IReadOnlyList<DeliveryRule> deliveryRules) =>
        deliveryRules.Where(rule => string.Equals(rule.Producer.Source, "event-symbol", StringComparison.Ordinal)).ToList();

    // Combined (declaringType, method) -> (Tag, Role, Resolve, HandlerDispatcher) map across ALL arg rules, so
    // one invocation-ref scan serves every actor-shaped mechanism. The Registration endpoint's
    // Methods×DeclaringTypes map to Role=Registration; the Producer endpoint's to Role=Producer. A method
    // appearing under both (none today) resolves to whichever rule is listed last. Exposed so a store-backed
    // caller can skip the invocation-ref SQL scan entirely when it is empty.
    public static Dictionary<(string Type, string Name), (string Tag, DeliveryRole Role, string Resolve, string? HandlerDispatcher)> ArgMethods(
        IReadOnlyList<DeliveryRule> deliveryRules
    )
    {
        var argMethods =
            new Dictionary<(string Type, string Name), (string Tag, DeliveryRole Role, string Resolve, string? HandlerDispatcher)>();
        foreach (var rule in deliveryRules)
        {
            AddArgEndpoint(argMethods, rule.Tag, rule.Registration, DeliveryRole.Registration);
            AddArgEndpoint(argMethods, rule.Tag, rule.Producer, DeliveryRole.Producer);
        }

        return argMethods;
    }

    // Projects the delivery sites for `deliveryRules` over the supplied rows. Event sites first (one per
    // event read per event rule), then the arg sites in row order — the emission order the store loader has
    // always produced. Rows the rules don't need may be passed empty (an empty event-read list with event
    // rules present, or an empty invocation list with arg rules present, simply yields no sites from that arm).
    public static IReadOnlyList<DeliverySite> Project(
        IReadOnlyList<DeliveryRule> deliveryRules,
        IReadOnlyList<EventRead> eventReads,
        IReadOnlyList<ArgInvocation> argInvocations
    )
    {
        var sites = new List<DeliverySite>();

        // --- event-symbol rules: one event-read pass, regardless of rule count (normally one event rule). ---
        var eventRules = EventRules(deliveryRules);
        if (eventRules.Count > 0)
        {
            foreach (var rule in eventRules)
            {
                foreach (var r in eventReads)
                {
                    sites.Add(
                        new DeliverySite(
                            Caller: r.EnclosingSymbolId!,
                            FilePath: r.FilePath,
                            Line: r.Line,
                            IdentityToken: r.TargetSymbolId,
                            Tag: rule.Tag,
                            Role: DeliveryRole.ByColocation
                        )
                    );
                }
            }
        }

        // --- arg rules: the combined (declaringType, method) -> (Tag, Role) map across ALL arg rules, so one
        //     invocation-ref pass serves every actor-shaped mechanism. ---
        var argMethods = ArgMethods(deliveryRules);

        if (argMethods.Count > 0)
        {
            foreach (var r in argInvocations)
            {
                var parsed = ParseInvocationTarget(r.TargetSymbolId);
                if (parsed is not { } method || !argMethods.TryGetValue(method, out var tagRole))
                {
                    continue;
                }

                // Both arg resolvers GATE to a member path (contains a '.', e.g. `ProcessDns.AccountService`):
                // a bare-variable name (`tell(pid, …)`) is not a stable cross-method identity and collides
                // spuriously with framework internals, so it never becomes a delivery site.
                if (!r.FirstArgumentName!.Contains('.', StringComparison.Ordinal))
                {
                    continue;
                }

                // `path` keeps the full member path (`ProcessDns.AccountService`); `leaf` takes the LAST segment
                // (`AccountService`) — the bridge across PARALLEL registries that share a leaf but differ by
                // class prefix (e.g. a tell through `ProcessDns.X` and the spawn through `ProcessNames.X` name
                // the same process X). `leaf` is more ~heuristic — a leaf shared by two unrelated channels
                // over-joins — so it is opt-in per rule (the resolve field), calibrated, and disclosed.
                var token = string.Equals(tagRole.Resolve, "leaf", StringComparison.Ordinal)
                    ? r.FirstArgumentName![(r.FirstArgumentName!.LastIndexOf('.') + 1)..]
                    : r.FirstArgumentName!;

                sites.Add(
                    new DeliverySite(
                        Caller: r.EnclosingSymbolId!,
                        FilePath: r.FilePath,
                        Line: r.Line,
                        IdentityToken: token,
                        Tag: tagRole.Tag,
                        Role: tagRole.Role,
                        HandlerDispatcher: tagRole.HandlerDispatcher
                    )
                );
            }
        }

        return sites;
    }

    // Folds one arg-source endpoint's Methods×DeclaringTypes into the combined (type, method) -> (Tag, Role)
    // map. Non-arg endpoints (e.g. event-symbol) are skipped here — they are handled by the event pass.
    // NOTE on ArgumentIndex: only FirstArgumentName/arg0 is captured as a fact today, so the `path` resolver
    // always reads arg0; an endpoint declaring ArgumentIndex != 0 is treated as arg0 (no crash) — an
    // extraction limitation to lift when nth-argument names are captured.
    private static void AddArgEndpoint(
        Dictionary<(string Type, string Name), (string Tag, DeliveryRole Role, string Resolve, string? HandlerDispatcher)> map,
        string tag,
        DeliveryEndpoint endpoint,
        DeliveryRole role
    )
    {
        if (!string.Equals(endpoint.Source, "arg", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var declaringType in endpoint.DeclaringTypes ?? [])
        {
            foreach (var name in endpoint.Methods ?? [])
            {
                map[(declaringType, name)] = (tag, role, endpoint.Resolve, endpoint.HandlerDispatcher);
            }
        }
    }

    // "M:Echo.Process.tell``1(Echo.ProcessId,…)" -> ("Echo.Process", "tell"). A generic "(declaringType,
    // method) from an M: DocID" parser. Mirrors FactEffectDeriver's ParseMethod (declaring type's arity
    // markers stripped is unnecessary for the actor types, which are non-generic, so we keep the declaring
    // type verbatim and only trim the method-level "``N"). Null when the DocID is not a method id or has no
    // dot before the member name.
    private static (string DeclaringType, string Name)? ParseInvocationTarget(string docId)
    {
        if (!docId.StartsWith("M:", StringComparison.Ordinal))
        {
            return null;
        }

        var searchEnd = docId.IndexOf('(');
        if (searchEnd < 0)
        {
            searchEnd = docId.Length;
        }

        var lastDot = docId.LastIndexOf('.', searchEnd - 1);
        if (lastDot < 2)
        {
            return null;
        }

        var declaring = docId.Substring(startIndex: 2, length: lastDot - 2);
        var methodStart = lastDot + 1;
        var backtick = docId.IndexOf('`', startIndex: methodStart, count: searchEnd - methodStart);
        var methodEnd = backtick >= 0 ? backtick : searchEnd;
        if (methodEnd <= methodStart)
        {
            return null;
        }

        return (declaring, docId.Substring(startIndex: methodStart, length: methodEnd - methodStart));
    }
}
