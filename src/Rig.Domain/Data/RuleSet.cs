using System.Text.RegularExpressions;
using Rig.Domain.Functions;

namespace Rig.Domain.Data;

// The effective, immutable rule blob: the whole cascade (built-in + global ~/.rig + local rig.rules.json
// + --rules) merged ONCE and projected to every collection a receiver consumes. This is the single rule
// currency that crosses every layer — query commands, the graph materializer, and the index/extraction
// pass all take a RuleSet by value. It is pure data: loading lives in Rig.Analysis (RuleSetLoader), which
// is the only layer that can read the JSON authoring model. Construct it there; everyone else receives it.
//
// Slices fall in two families. The Fact* slices (Handoff/Factory/Cut/Context/Effects/Observations/
// EntryPoints/ClassInheritance/Render/Delivery) are the fact-matchable projections the Domain matchers consume. The
// remaining slices (DiRegistrations/File*/TestProjectPatterns/ProjectExcludePatterns/StaticDiMappings/
// XmlDiFiles) are consumed by the index/extraction pass in their authoring form.
public sealed record RuleSet
{
    // Content fingerprint of the effective rule cascade that produced this projection. Production rule
    // loads always set it; hand-built RuleSet instances may leave it null, which deliberately disables
    // the persisted-graph rules gate for tests and rule-independent callers.
    public string? EffectiveFingerprint { get; init; }

    // False when a query projection intentionally removes an EDGE-CREATING rule (currently --raw clearing
    // Factory). The persisted call_edges graph was built with the full effective RuleSet and is therefore
    // not an equivalent input even though its fingerprint still matches; store loaders must use facts.
    public bool MaterializedGraphCompatible { get; init; } = true;

    public IReadOnlyList<FactHandoffRule> Handoff { get; init; } = [];

    // External-virtual-override-orphan redirects (docs/backlog.md): rewrite a call to an external convenience
    // overload to the virtual hatch it trampolines into, applied at the reference→edge projection.
    public IReadOnlyList<FactRedirectRule> Redirect { get; init; } = [];

    // EXTERNAL-NODE ADMISSION overrides (the `externalNodes` section): extra ALLOWED / DENIED assembly
    // names layered over ExternalNodeAdmission's framework deny-list + rule-derived type patterns. A single
    // object; null when the section is absent, which means "the defaults" — the feature is default-ON and
    // this section is its only knob.
    public FactExternalNodeRule? ExternalNodes { get; init; }

    // FR-7 cache-coherence POLICY (declared cached entities + an optional generated-ORM-noise namespace-suffix
    // filter) for the cache-coherence INSTANCE of the generic effect-correlation deriver (wired in
    // DeriveCommand). A single object; null when the `cacheCoherence` section is absent.
    public FactCacheCoherenceRule? CacheCoherence { get; init; }

    // cross_method_amplification POLICY (the read gate + the reach bound + the witness grain) for the PRESENCE
    // instance of the generic effect-correlation deriver (wired in DeriveCommand). A single object; null when
    // the `crossMethodAmplification` section is absent. Opt-in — absent section = detector off.
    public FactCrossMethodAmplificationRule? CrossMethodAmplification { get; init; }

    // FR-8 dual_write POLICY: the durable-write `provider:operation` -> system-class map (threaded into
    // FactHazardDeriver.DeriveDualWrites by the effect-derivation callers). A single object; null when the
    // `dualWrite` section is absent, which leaves the detector off — core ships no default map, because every
    // key in one is a project's own effect vocabulary.
    public FactDualWriteRule? DualWrite { get; init; }

    // static_init_capture POLICY (the project-specific mutable-source resource patterns) for the
    // static-init-capture detector (wired in DeriveCommand). A single object; null when the
    // `staticInitCapture` section is absent. Opt-in — the detector fires only when this is present.
    public FactStaticInitCaptureRule? StaticInitCapture { get; init; }

    public IReadOnlyList<FactGenericFactoryRule> Factory { get; init; } = [];
    public IReadOnlyList<FactTraversalCutRule> Cut { get; init; } = [];
    public IReadOnlyList<FactContextDispatchRule> Context { get; init; } = [];
    public IReadOnlyList<FactEffectRule> Effects { get; init; } = [];
    public FactObservationRules Observations { get; init; } = new([], [], [], [], [], [], []);
    public IReadOnlyList<FactEntryPointRule> EntryPoints { get; init; } = [];
    public IReadOnlyList<FactClassInheritanceRule> ClassInheritance { get; init; } = [];
    public FactRenderRules Render { get; init; } = new(CollapseSeams: [], OpaqueTypes: []);
    public IReadOnlyList<DeliveryRule> Delivery { get; init; } = [];
    public IReadOnlyDictionary<string, string> EffectEmoji { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // provider -> the FAMILY it belongs to. The first DECLARATION of a noun that ten rule sections already
    // select on by bare string (effects, effectEmoji, dualWrite.systemClassMap, observations.*, cacheCoherence,
    // crossMethodAmplification) — which is why a typo in one of those lists silently disables a gate today.
    //
    // A family groups providers that are the same SUBSYSTEM to a reader (llblgen + dapper + db_command = db),
    // shrinking a 70-provider vocabulary to the handful an IDE can render. It is NOT the same axis as
    // dualWrite.systemClassMap: that answers "is this a DURABLE WRITE and to which system" and is therefore
    // operation-keyed (http:POST yes, http:GET no), so the two deliberately stay separate.
    //
    // ABSENT ⇒ family = the provider's own name (identity, never a built-in literal — this file ships no
    // project vocabulary). Merge is PER KEY, so an overlay adds providers without restating the builtin's.
    public IReadOnlyDictionary<string, string> ProviderFamilies { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // Index/extraction-side slices, consumed in authoring form by SolutionSourceLoader / SourceFileClassifier
    // / XmlDiMiner / DiRegistrationExtractor.
    public IReadOnlyList<DiRegistrationRule> DiRegistrations { get; init; } = [];
    public IReadOnlyList<FileRule> FileInclude { get; init; } = [];
    public IReadOnlyList<FileRule> FileExclude { get; init; } = [];
    public IReadOnlyList<string> TestProjectPatterns { get; init; } = [];
    public IReadOnlyList<string> ProjectExcludePatterns { get; init; } = [];
    public IReadOnlyList<StaticDiMapping> StaticDiMappings { get; init; } = [];
    public IReadOnlyList<string> XmlDiFiles { get; init; } = [];

    public FileRule? FindIncludedFile(string relativePath) => FileInclude.FirstOrDefault(rule => rule.IsMatch(relativePath));

    public FileRule? FindExcludedFile(string relativePath) => FileExclude.LastOrDefault(rule => rule.IsMatch(relativePath));

    public bool IsTestProject(string projectName) =>
        TestProjectPatterns.Any(pattern => GlobMatcher.IsMatch(value: projectName, glob: pattern));

    public bool IsExcludedProject(string projectName) =>
        ProjectExcludePatterns.Any(pattern => GlobMatcher.IsMatch(value: projectName, glob: pattern));
}

public sealed record FileRule(string Id, string Glob, string Reason, Regex Regex)
{
    public bool IsMatch(string relativePath) => Regex.IsMatch(relativePath.Replace('\\', '/'));
}

public sealed record DiRegistrationRule(IReadOnlyList<string> Methods, string Lifetime, string RegistrationKind, string Reason)
{
    public bool Matches(string methodName) => Methods.Contains(methodName, StringComparer.Ordinal);
}

// Pre-declared interface->implementation mapping sourced from external DI descriptors
// (e.g. XML service files, web.config appSettings) rather than from code patterns.
public sealed record StaticDiMapping(
    string ServiceType,
    string ImplementationType,
    string Lifetime = "singleton",
    string RegistrationKind = "static"
);
