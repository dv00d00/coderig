using Rig.Analysis.Rules;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Analysis;

public sealed class RuleSetLoaderTests
{
    // A project overlay's amplification display CATEGORIES must survive the cascade merge. Core ships none
    // (naming one would bake a project's effect vocabulary into rig), so if this section is dropped during the
    // merge the failure is SILENT: `amplify` still emits findings, they just all land unranked in the main
    // section with no separate/excluded handling. That is exactly what happened in review, and it is the third
    // time an observations list has been forgotten in MergeObservations — hence a pinned test.
    [Test]
    public void Amplification_categories_survive_the_cascade_merge()
    {
        using var workspace = TempRulesWorkspace.Create(
            // lang=json
            """
            {
              "observations": {
                "amplificationCategories": [
                  {
                    "name": "fire-and-forget",
                    "label": "Fire-and-forget queueing",
                    "weight": 50,
                    "separate": true,
                    "providers": ["actor"],
                    "operations": ["tell"]
                  },
                  { "name": "contention", "excluded": true, "providers": ["lock"] }
                ]
              }
            }
            """
        );

        var ruleSet = RuleSetLoader.LoadForSolution(workspace.SolutionPath);

        var categories = ruleSet.Observations.AmplificationCategoriesOrEmpty;
        var separate = categories.Where(c => c.Separate).ShouldHaveSingleItem();
        separate.Name.ShouldBe("fire-and-forget");
        separate.Label.ShouldBe("Fire-and-forget queueing");
        separate.Weight.ShouldBe(50);
        separate.Providers.ShouldContain("actor");
        separate.Operations.ShouldContain("tell");

        categories.ShouldContain(c => c.Excluded && c.Name == "contention");

        // The builtin observation lists must still be there — a merge that drops a sibling section is the
        // same bug wearing a different hat.
        ruleSet.Observations.AmplificationOrEmpty.ShouldNotBeEmpty();
        ruleSet.Observations.EnumeratingMethods.ShouldNotBeEmpty();
    }

    // The FR-8 dual_write system-class map must survive the cascade merge, and it must merge PER KEY: an
    // overlay adds its own providers ON TOP of the shipped generic ones instead of replacing the section. A
    // section forgotten in Merge is the recurring silent failure — dual_write would keep classifying only the
    // builtin providers, so a project's own writes (its ORM, its actor mailbox) would never pair and the
    // detector would report nothing while looking configured.
    [Test]
    public void DualWrite_system_class_map_survives_the_cascade_merge()
    {
        using var workspace = TempRulesWorkspace.Create(
            // lang=json
            """
            {
              "dualWrite": {
                "systemClassMap": {
                  "orm:write": "db",
                  "mailbox:tell": "queue",
                  "efcore:commit": "relational"
                }
              }
            }
            """
        );

        var map = RuleSetLoader.Load(workspace.DirectoryPath).DualWrite?.SystemClassMap;

        map.ShouldNotBeNull();
        // The overlay's own providers arrived…
        map!["orm:write"].ShouldBe("db");
        map["mailbox:tell"].ShouldBe("queue");
        // …the builtin generic entries are still there (a whole-object merge would have dropped them)…
        map["rabbitmq:publish"].ShouldBe("queue");
        map["smtp:send"].ShouldBe("email");
        map["http:POST"].ShouldBe("http");
        // …and a key the overlay RESTATES wins, which is what makes the map overridable at all.
        map["efcore:commit"].ShouldBe("relational");
        // Keys are compared ordinally: `http:POST` is not `http:post`.
        map.ContainsKey("http:post").ShouldBeFalse();
    }

    // Core ships no system-class map of its own: the shipped builtin JSON carries the entries for the providers
    // IT declares, and a ruleset that drops the section leaves dual_write OFF rather than classifying against
    // some other project's vocabulary.
    [Test]
    public void The_shipped_builtin_dual_write_map_names_only_generic_providers()
    {
        using var workspace = TempRulesWorkspace.Create("{}");

        var map = RuleSetLoader.Load(workspace.DirectoryPath).DualWrite?.SystemClassMap;

        map.ShouldNotBeNull();
        map!.ShouldNotBeEmpty();
        var providers = map.Keys.Select(k => k.Split(':')[0]).Distinct().ToList();
        // The MedDBase overlay's providers must NOT ship with the tool (core-purity F3/F5).
        providers.ShouldNotContain("llblgen");
        providers.ShouldNotContain("entity_cache");
        providers.ShouldNotContain("echo_publish");
        providers.ShouldNotContain("eventbus");
        providers.ShouldNotContain("actor");
    }

    // event_cycle's delivery vocabulary is rule data (core-purity F6): a mechanism joins the cycle hunt by
    // declaring `cycleDelivery`, and states how exact its producer→handler join is with `joinConfidence`. Both
    // fields must survive the cascade merge AND the projection — dropped, the deriver would see an empty
    // dispatcher set and silently report no cycles at all.
    [Test]
    public void Delivery_cycle_fields_survive_the_cascade_merge()
    {
        using var workspace = TempRulesWorkspace.Create(
            // lang=json
            """
            {
              "deliveryRules": [
                {
                  "id": "mailbox",
                  "tag": "mailbox_tell",
                  "confidence": "heuristic",
                  "cycleDelivery": true,
                  "joinConfidence": "low",
                  "producer": { "source": "arg", "resolve": "leaf", "methods": ["tell"], "declaringTypes": ["App.Mailbox"] },
                  "registration": { "source": "arg", "resolve": "leaf", "methods": ["spawn"], "declaringTypes": ["App.Mailbox"] }
                }
              ]
            }
            """
        );

        var delivery = RuleSetLoader.Load(workspace.DirectoryPath).Delivery;

        var rule = delivery.Single(r => r.Id == "mailbox");
        rule.CycleDelivery.ShouldBeTrue();
        rule.JoinConfidence.ShouldBe("low");

        // The builtin C# event rule is still there (a merge that drops the shipped list is the same bug), and
        // it is the exact-join arm.
        var csharpEvent = delivery.Single(r => r.Id == "csharp-event");
        csharpEvent.CycleDelivery.ShouldBeTrue();

        // The projection the deriver actually consumes carries both mechanisms with their join confidence.
        var dispatchers = FactCycleDeriver.CycleDeliveryDispatchers(delivery);
        dispatchers["mailbox_tell"].ShouldBe("low");
        dispatchers["event_raise"].ShouldBe("high");
    }

    // resourceSpan rules merge BY ID where one is declared: an overlay restating the shipped
    // "transaction-span" rule REPLACES it rather than adding a second rule. `excludeProviders` is a
    // SUPPRESSION list, so appending could never extend it — both rules would fire and the un-suppressed one
    // would annotate the effect anyway.
    [Test]
    public void A_resource_span_rule_with_a_known_id_replaces_it_instead_of_appending()
    {
        using var workspace = TempRulesWorkspace.Create(
            // lang=json
            """
            {
              "observations": {
                "resourceSpan": [
                  {
                    "id": "transaction-span",
                    "scopeKind": "using",
                    "scopeTypePatterns": ["Transaction"],
                    "excludeProviders": ["efcore", "orm"],
                    "observationType": "transaction_spans_effect",
                    "context": "transaction"
                  },
                  {
                    "scopeKind": "using",
                    "scopeTypePatterns": ["Batch"],
                    "excludeProviders": [],
                    "observationType": "batch_spans_effect",
                    "context": "batch"
                  }
                ]
              }
            }
            """
        );

        var resourceSpan = RuleSetLoader.Load(workspace.DirectoryPath).Observations.ResourceSpan;

        // ONE transaction rule, and it is the overlay's (with the project's own ORM suppressed).
        var transaction = resourceSpan.Where(r => r.ObservationType == "transaction_spans_effect").ShouldHaveSingleItem();
        transaction.ExcludeProviders.ShouldBe(["efcore", "orm"]);

        // The id-less overlay rule appended, and the builtin lock rule was not disturbed.
        resourceSpan.ShouldContain(r => r.ObservationType == "batch_spans_effect");
        resourceSpan.ShouldContain(r => r.ObservationType == "lock_held_across_effect");
    }

    [Test]
    public void LoadForSolution_allows_comments_and_trailing_commas()
    {
        using var workspace = TempRulesWorkspace.Create(
            // lang=json
            // lang=json
            """
            {
              "redirectRules": [
               { "method": "M:Ext.EntityBase.Save1", "redirectTo": "M:Ext.EntityBase.Save(Ext.IPredicate,System.Boolean)" },
               // { "method": "M:Ext.EntityBase.Save2", "redirectTo": "M:Ext.EntityBase.Save(Ext.IPredicate,System.Boolean)" },
               { "method": "M:Ext.EntityBase.Save3", "redirectTo": "M:Ext.EntityBase.Save(Ext.IPredicate,System.Boolean)" },
             ]
            }
            """
        );

        var ruleSet = RuleSetLoader.LoadForSolution(workspace.SolutionPath);
        ruleSet.Redirect.Count.ShouldBe(2);
    }

    [Test]
    public void LoadForSolution_rejects_file_rules_without_id()
    {
        using var workspace = TempRulesWorkspace.Create(
            // lang=json
            """
            {
              "files": {
                "exclude": [{ "glob": "**/*.g.cs", "reason": "generated" }]
              }
            }
            """
        );

        var exception = Should.Throw<InvalidOperationException>(() => RuleSetLoader.LoadForSolution(workspace.SolutionPath));

        exception.Message.ShouldContain("File rule in `exclude` is missing `id`.");
    }

    [Test]
    public void LoadForSolution_rejects_file_rules_without_glob()
    {
        using var workspace = TempRulesWorkspace.Create(
            // lang=json
            """
            {
              "files": {
                "include": [{ "id": "include-contract", "reason": "contract_fixture" }]
              }
            }
            """
        );

        var exception = Should.Throw<InvalidOperationException>(() => RuleSetLoader.LoadForSolution(workspace.SolutionPath));

        exception.Message.ShouldContain("File rule `include-contract` is missing `glob`.");
    }

    [Test]
    public void LoadForSolution_merges_solution_and_extra_rules()
    {
        using var workspace = TempRulesWorkspace.Create(
            solutionRulesJson: // lang=json
            """
            {
              "files": {
                "testProjectPatterns": ["*.Tests"]
              }
            }
            """,
            extraRulesJson: // lang=json
            """
            {
              "projects": {
                "exclude": ["*.AppHost"]
              }
            }
            """
        );

        var rules = RuleSetLoader.LoadForSolution(workspace.SolutionPath, [workspace.ExtraRulesPath]);

        rules.IsTestProject("Rig.Tests").ShouldBeTrue();
        rules.IsExcludedProject("Sample.AppHost").ShouldBeTrue();
    }

    [Test]
    public void TypeEntryPoints_requires_is_parsed_and_projected_to_the_fact_rule()
    {
        using var workspace = TempRulesWorkspace.Create(
            // lang=json
            """
            {
              "entrypoints": {
                "typeEntryPoints": [
                  { "id": "t1", "kind": "page", "baseTypes": ["App.PageBase"], "namespacePrefix": "App.Pages.", "requires": ["FrontEnd", "BackEnd"] }
                ]
              }
            }
            """
        );

        var projected = RuleSetLoader.Load(workspace.DirectoryPath).EntryPoints;

        projected.ShouldHaveSingleItem().Requires.ShouldBe(["FrontEnd", "BackEnd"]);
    }

    [Test]
    public void PageModel_is_a_back_compat_alias_for_typeEntryPoints()
    {
        // The framework-specific `pageModel` key was generalised to `typeEntryPoints`; existing configs
        // using the old key must keep loading (merged into the same collection).
        using var workspace = TempRulesWorkspace.Create(
            // lang=json
            """
            {
              "entrypoints": {
                "pageModel": [
                  { "id": "legacy", "kind": "page", "baseTypes": ["App.PageBase"], "namespacePrefix": "App.Pages." }
                ]
              }
            }
            """
        );

        var rules = RuleSetLoader.LoadForSolution(workspace.SolutionPath);

        rules.EntryPoints.ShouldHaveSingleItem().Id.ShouldBe("legacy");
    }

    [Test]
    public void HandoffDispatcher_requires_is_parsed_and_projected()
    {
        using var workspace = TempRulesWorkspace.Create(
            // lang=json
            """
            {
              "handoffDispatchers": [
                { "id": "bg", "kind": "background", "consumerPatterns": ["Schedule.#ctor"], "requires": ["FrontEnd"] }
              ]
            }
            """
        );

        var projected = RuleSetLoader.Load(workspace.DirectoryPath).Handoff;

        projected.ShouldHaveSingleItem().Requires.ShouldBe(["FrontEnd"]);
    }

    [Test]
    public void DeliveryRules_round_trip_into_RuleSet_Delivery()
    {
        using var workspace = TempRulesWorkspace.Create(
            // lang=json
            """
            {
              "deliveryRules": [
                {
                  "id": "echo-actor",
                  "tag": "actor_tell",
                  "confidence": "heuristic",
                  "producer": {
                    "source": "arg", "resolve": "path", "argumentIndex": 0,
                    "methods": ["tell", "ask"], "declaringTypes": ["Echo.Process"]
                  },
                  "registration": {
                    "source": "arg", "resolve": "path", "argumentIndex": 0,
                    "methods": ["spawn", "register"], "declaringTypes": ["Echo.Process"]
                  }
                }
              ]
            }
            """
        );

        // The cascade merges the builtin deliveryRules first, then the workspace's; assert on the
        // workspace's `echo-actor` overlaid copy (the LAST one in load order carries the test's method lists).
        var delivery = RuleSetLoader.Load(workspace.DirectoryPath).Delivery;

        var rule = delivery.Last(r => r.Id == "echo-actor");
        rule.Tag.ShouldBe("actor_tell");
        rule.Confidence.ShouldBe("heuristic");
        rule.Producer.Source.ShouldBe("arg");
        rule.Producer.Resolve.ShouldBe("path");
        rule.Producer.Methods.ShouldBe(["tell", "ask"]);
        rule.Producer.DeclaringTypes.ShouldBe(["Echo.Process"]);
        rule.Registration.Methods.ShouldBe(["spawn", "register"]);
    }

    [Test]
    public void RedirectRules_round_trip_into_RuleSet_Redirect_through_the_cascade_merge()
    {
        // Regression: the cascade Merge must carry the local `redirectRules` section into RuleSet.Redirect.
        // It was initially omitted from Merge, so a colocated rule silently vanished — caught only on the real
        // store (the suite missed it because tests that construct rules directly bypass the loader cascade).
        using var workspace = TempRulesWorkspace.Create(
            // lang=json
            """
            {
              "redirectRules": [
                { "method": "M:Ext.EntityBase.Save", "redirectTo": "M:Ext.EntityBase.Save(Ext.IPredicate,System.Boolean)" }
              ]
            }
            """
        );

        var redirect = RuleSetLoader.Load(workspace.DirectoryPath).Redirect;

        var rule = redirect.ShouldHaveSingleItem();
        rule.Method.ShouldBe("M:Ext.EntityBase.Save");
        rule.RedirectTo.ShouldBe("M:Ext.EntityBase.Save(Ext.IPredicate,System.Boolean)");
    }

    [Test]
    public void CacheCoherence_round_trips_into_RuleSet_through_the_cascade_merge()
    {
        // Regression mirror of the redirectRules test: the cascade Merge must carry the local `cacheCoherence`
        // section (a single object, last-writer-wins) into RuleSet.CacheCoherence. A section omitted from Merge
        // silently vanishes from the cascade. Now covers the WHOLE detector spec — anchor, companion, both
        // normalizers and the discovery read are rule data (core-purity F1+F2), so a field dropped in the
        // projection would silently retarget or disarm the detector.
        using var workspace = TempRulesWorkspace.Create(
            // lang=json
            """
            {
              "cacheCoherence": {
                "anchor": { "provider": "orm", "operation": "bulk_write" },
                "companion": { "provider": "cache", "operation": "invalidate" },
                "anchorStripSuffix": ["EntityCollection", "DAO"],
                "companionStripSuffix": ["Cache"],
                "discoveryRead": { "provider": "entity_cache", "operation": "read", "stripSuffix": ["Cache", "Entity"] },
                "cachedEntities": ["Account", "Person"],
                "excludeEnclosingNamespaceSuffix": ["CollectionClasses", "DaoClasses"]
              }
            }
            """
        );

        var cacheCoherence = RuleSetLoader.Load(workspace.DirectoryPath).CacheCoherence;

        cacheCoherence.ShouldNotBeNull();
        cacheCoherence!.CachedEntities.ShouldBe(["Account", "Person"]);
        cacheCoherence.ExcludeEnclosingNamespaceSuffix.ShouldBe(["CollectionClasses", "DaoClasses"]);
        cacheCoherence.Anchor.Provider.ShouldBe("orm");
        cacheCoherence.Anchor.Operation.ShouldBe("bulk_write");
        cacheCoherence.Companion.Provider.ShouldBe("cache");
        cacheCoherence.Companion.Operation.ShouldBe("invalidate");
        cacheCoherence.AnchorStripSuffix.ShouldBe(["EntityCollection", "DAO"]);
        cacheCoherence.CompanionStripSuffix.ShouldBe(["Cache"]);
        cacheCoherence.DiscoveryRead.ShouldNotBeNull();
        cacheCoherence.DiscoveryRead!.Provider.ShouldBe("entity_cache");
        cacheCoherence.DiscoveryRead.Operation.ShouldBe("read");
        cacheCoherence.DiscoveryRead.StripSuffix.ShouldBe(["Cache", "Entity"]);
    }

    // Core ships NO anchor/companion pair, because either one would be a single project's provider vocabulary
    // shipped as every codebase's default — where it matches nothing and the detector reports "no findings" as
    // if the code were clean. A section that names neither must therefore project to null (detector OFF), not
    // to a built-in spec.
    [Test]
    public void CacheCoherence_without_an_anchor_and_companion_leaves_the_detector_off()
    {
        using var workspace = TempRulesWorkspace.Create(
            // lang=json
            """
            {
              "cacheCoherence": {
                "cachedEntities": ["Account", "Person"],
                "excludeEnclosingNamespaceSuffix": ["CollectionClasses", "DaoClasses"]
              }
            }
            """
        );

        RuleSetLoader.Load(workspace.DirectoryPath).CacheCoherence.ShouldBeNull();
    }

    // The discovery tier is independently optional: an anchor+companion with no `discoveryRead` is a VALID,
    // ON detector running the declared-contract tier alone — it must not fall back to a built-in cache-read
    // provider (the F2 literal), and it must not disarm the whole section either.
    [Test]
    public void CacheCoherence_without_a_discovery_read_stays_on_with_declared_entities_only()
    {
        using var workspace = TempRulesWorkspace.Create(
            // lang=json
            """
            {
              "cacheCoherence": {
                "anchor": { "provider": "orm", "operation": "bulk_write" },
                "companion": { "provider": "cache", "operation": "invalidate" },
                "cachedEntities": ["Account"]
              }
            }
            """
        );

        var cacheCoherence = RuleSetLoader.Load(workspace.DirectoryPath).CacheCoherence;

        cacheCoherence.ShouldNotBeNull();
        cacheCoherence!.DiscoveryRead.ShouldBeNull();
        cacheCoherence.AnchorStripSuffix.ShouldBeEmpty();
        cacheCoherence.CompanionStripSuffix.ShouldBeEmpty();
        cacheCoherence.CachedEntities.ShouldBe(["Account"]);
    }

    private sealed class TempRulesWorkspace : IDisposable
    {
        private TempRulesWorkspace(string directory, string solutionPath, string extraRulesPath)
        {
            DirectoryPath = directory;
            SolutionPath = solutionPath;
            ExtraRulesPath = extraRulesPath;
        }

        public string DirectoryPath { get; }
        public string SolutionPath { get; }
        public string ExtraRulesPath { get; }

        public static TempRulesWorkspace Create(string solutionRulesJson, string? extraRulesJson = null)
        {
            var directory = Directory.CreateTempSubdirectory("rig-rules-").FullName;
            var solutionPath = Path.Combine(directory, "Sample.slnx");
            var extraRulesPath = Path.Combine(directory, "extra.rules.json");

            File.WriteAllText(solutionPath, "<Solution />");
            File.WriteAllText(Path.Combine(directory, "rig.rules.json"), solutionRulesJson);
            File.WriteAllText(extraRulesPath, extraRulesJson ?? "{}");

            return new TempRulesWorkspace(directory, solutionPath, extraRulesPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
