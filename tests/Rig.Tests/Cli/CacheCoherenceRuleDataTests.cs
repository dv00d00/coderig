using Rig.Cli.Commands;
using Rig.Domain.Data;
using Shouldly;

namespace Rig.Tests.Cli;

// Core-purity F1+F2: the cache_coherence detector's whole vocabulary — anchor, companion, both key
// normalizers, and the DISCOVERY read that widens the in-scope key set — is rule data. These tests pin the
// half that has behavior rather than shape: DeriveCommand.BuildCacheInScopeKeys, the tiering that decides
// which entities the correlation may flag at all.
//
// Before the fix, the discovery tier was `entity_cache:read` stripped of "Cache"/"Entity" — three literals of
// one codebase's vocabulary in core C#. A ruleset with a differently-named cache provider silently got an
// empty discovery tier (declared entities only) with no diagnostic.
public sealed class CacheCoherenceRuleDataTests
{
    private static DerivedEffect Effect(string provider, string operation, string resourceType) =>
        new(
            Provider: provider,
            Operation: operation,
            ResourceType: resourceType,
            EnclosingSymbolId: "M:App.Svc.Run",
            FilePath: "App/Svc.cs",
            Line: 10
        );

    private static FactCacheCoherenceRule Rule(FactCacheDiscoveryRead? discoveryRead, params string[] cachedEntities) =>
        new(
            Anchor: new FactEffectSelector("orm", "bulk_write"),
            Companion: new FactEffectSelector("cache", "invalidate"),
            CachedEntities: cachedEntities,
            AnchorStripSuffix: ["EntityCollection"],
            CompanionStripSuffix: ["Cache"],
            DiscoveryRead: discoveryRead
        );

    // The discovery provider:operation comes from the RULE — a store whose cache reads are `entity_cache:read`
    // and one whose are `redis:get` must both discover their keys, with no provider named in C#.
    [Test]
    public void The_discovery_tier_reads_its_provider_operation_and_strip_suffixes_from_the_rule()
    {
        var effects = new[]
        {
            Effect("entity_cache", "read", "T:App.AccountCache"),
            Effect("entity_cache", "read", "T:App.PersonEntity"),
            Effect("entity_cache", "write", "T:App.SiteCache"), // wrong OPERATION — not a discovery read
            Effect("redis", "read", "T:App.LocationCache"), // wrong PROVIDER — not a discovery read
        };

        var keys = DeriveCommand.BuildCacheInScopeKeys(
            rule: Rule(new FactCacheDiscoveryRead("entity_cache", "read", ["Cache", "Entity"])),
            effects: effects
        );

        // Both resource forms normalize onto the bare entity name the anchor/companion keys land on.
        keys.ShouldContainKeyAndValue("Account", "medium");
        keys.ShouldContainKeyAndValue("Person", "medium");
        keys.ShouldNotContainKey("Site");
        keys.ShouldNotContainKey("Location");
    }

    // Same effects, a rule pointed at a DIFFERENT cache stack: the keys follow the rule, proving nothing in
    // core is hardwired to the entity_cache vocabulary.
    [Test]
    public void Retargeting_the_discovery_read_at_another_stack_moves_the_discovered_keys()
    {
        var effects = new[] { Effect("entity_cache", "read", "T:App.AccountCache"), Effect("redis", "get", "T:App.LocationCacheEntry") };

        var keys = DeriveCommand.BuildCacheInScopeKeys(
            rule: Rule(new FactCacheDiscoveryRead("redis", "get", ["CacheEntry"])),
            effects: effects
        );

        keys.ShouldContainKeyAndValue("Location", "medium");
        keys.ShouldNotContainKey("Account");
    }

    // No `discoveryRead` = the DECLARED tier alone. Not a fallback to some built-in cache provider (that was
    // the F2 literal), and not a disarmed detector either.
    [Test]
    public void Without_a_discovery_read_only_the_declared_entities_are_in_scope()
    {
        var effects = new[] { Effect("entity_cache", "read", "T:App.AccountCache") };

        var keys = DeriveCommand.BuildCacheInScopeKeys(rule: Rule(discoveryRead: null, "Person"), effects: effects);

        keys.ShouldContainKeyAndValue("Person", "high");
        keys.ShouldNotContainKey("Account");
    }

    // The declared contract outranks discovery on overlap — a declared entity keeps flagging even if every
    // cache read of it is deleted.
    [Test]
    public void A_declared_entity_wins_the_certainty_tier_over_a_discovered_one()
    {
        var effects = new[] { Effect("entity_cache", "read", "T:App.AccountCache") };

        var keys = DeriveCommand.BuildCacheInScopeKeys(
            rule: Rule(new FactCacheDiscoveryRead("entity_cache", "read", ["Cache"]), "Account"),
            effects: effects
        );

        keys.ShouldContainKeyAndValue("Account", "high");
    }
}
