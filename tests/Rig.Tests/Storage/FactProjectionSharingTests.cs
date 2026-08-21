using System.Reflection;
using Rig.Analysis.Rules;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Storage;

// The gate for the SHARED row->record projections that replaced the hand-maintained copies: CallEdgeProjection
// (reference_facts -> CallEdge), FactFieldAccessProjection (reference_facts -> FactFieldAccess) and the four
// SymbolFactProjections (symbol_facts -> MethodRef / MethodSymbol / TypeSymbol / DeadCodeFinder.MethodMeta).
//
// Why these tests and not just the parity gates: FactGraphProjectionParityTests, LiveFactSourceParityTests and
// ReachInputProjectionTests all compare two PATHS to each other, so they catch a field that arrives on one path
// and not the other. They cannot catch a field dropped from the ONE shared projection — after consolidation
// that loses it on every path at once, which is exactly the failure mode that made `EnclosingScopes` vanish
// from reaches/tree/path (see FactInvocationProjection). So the projections are pinned here directly: every
// mappable field of a fully-populated source row must arrive, checked by REFLECTION over the target record so
// a NEW field is covered without anyone remembering to add an assertion.
//
// Plus the one copy consolidation removed that had no parity gate at all: the BOUNDED raw-ADO MethodRef reader
// (SqlReachability), whose hand-written ordinals used to be able to slip against the SELECT list.
[ClassDataSource<AnalyzedPlaygrounds>(Shared = SharedType.PerTestSession)]
public sealed class FactProjectionSharingTests(AnalyzedPlaygrounds playgrounds)
{
    // A reference fact with EVERY field set to a distinguishable non-default value, so a projection that drops
    // one leaves a default behind for the reflection sweep to find.
    private static ReferenceFact FullyPopulatedReference() =>
        new ReferenceFact(
            TargetSymbolId: "M:Ns.Callee.Do",
            RefKind: RefKinds.MethodGroup,
            EnclosingSymbolId: "M:Ns.Caller.Run",
            TargetAssembly: "Ns.Callee.dll",
            TargetInSource: true,
            FilePath: @"C:\src\Caller.cs",
            Line: 42,
            ReceiverType: "T:Ns.Receiver",
            FirstArgumentTemplate: "https://example/{id}",
            FirstArgumentType: "T:System.String",
            EnclosingLoopKind: "foreach",
            EnclosingLoopDetail: "row in rows",
            EnclosingInvocations: "Task/Tasks.Task/WhenAll",
            EnclosingCatchTypes: "System.Exception",
            TypeArguments: "Ns.Payload",
            FirstArgumentName: "Ns.ProcessDns.Worker",
            DelegateConsumer: "M:Ns.Scheduler.#ctor",
            EnclosingScopes: "lock/Ns.Gate",
            ArgumentTemplates: "[\"a\"]",
            ArgumentNames: "[\"b\"]",
            DeclaringTypeArgBinding: "[\"C:Ns.Account\"]",
            MethodTypeArgBinding: "[\"M:0\"]",
            NonVirtual: true,
            EnclosingGuards: "isEnabled",
            EnclosingLoopElementType: "T:Ns.Row",
            EnclosingLoopBindType: "T:Ns.Rows",
            InExpressionTree: true
        );

    // A symbol fact with every field set likewise. Modifiers carries `abstract` (TypeSymbol.IsAbstract) and the
    // path ends in `.g.cs` (MethodMeta.IsGenerated), so both derived booleans are non-default too.
    private static SymbolFact FullyPopulatedSymbol() =>
        new SymbolFact(
            SymbolId: "M:Ns.Type.Method",
            Kind: SymbolKinds.Method,
            Name: "Method",
            Namespace: "Ns",
            ContainingSymbolId: "T:Ns.Type",
            Modifiers: "public abstract",
            TypeKind: "class",
            Signature: "public abstract void Method(int a)",
            FilePath: @"C:\src\Type.g.cs",
            Line: 17,
            EndLine: 25,
            DefiningAssembly: "Ns.dll",
            IsOverride: true,
            BodyHash: "deadbeef"
        );

    // HandoffDispatcher and DeliveryPrecision are null BY CONSTRUCTION on a raw fact-derived edge (see
    // CallEdgeProjection): the dispatcher is attached later by HandoffClassifier, and DeliveryPrecision only
    // exists on the synthetic edges AddDeliveryEdges creates. Everything else must arrive.
    [Test]
    public void Call_edge_projection_carries_every_field_it_maps()
    {
        var edge = CallEdgeProjection.Project(FullyPopulatedReference());

        AssertNoDefaults(edge, exempt: [nameof(CallEdge.HandoffDispatcher), nameof(CallEdge.DeliveryPrecision)]);
        edge.Caller.ShouldBe("M:Ns.Caller.Run");
        edge.Callee.ShouldBe("M:Ns.Callee.Do");
        edge.Kind.ShouldBe(RefKinds.MethodGroup);
        // The redirect override (external-virtual-override-orphan fix) replaces the CALLEE and nothing else.
        var redirected = CallEdgeProjection.Project(FullyPopulatedReference(), redirectTo: "M:Ns.Base.Hatch");
        redirected.Callee.ShouldBe("M:Ns.Base.Hatch");
        redirected.ShouldBe(edge with { Callee = "M:Ns.Base.Hatch" });
    }

    [Test]
    public void Field_access_projection_carries_every_field_it_maps()
    {
        var access = FactFieldAccessProjection.Project(FullyPopulatedReference());

        AssertNoDefaults(access, exempt: []);
        access.Target.ShouldBe("M:Ns.Callee.Do");
        access.Enclosing.ShouldBe("M:Ns.Caller.Run");
        // The structural-context fields are the whole point of this record existing next to SymbolRef — the
        // ones an earlier drift silently dropped, taking lock_held_across_effect with them.
        access.EnclosingScopes.ShouldBe("lock/Ns.Gate");
        access.CatchTypes.ShouldBe("System.Exception");
        access.EnclosingInvocations.ShouldBe("Task/Tasks.Task/WhenAll");
    }

    [Test]
    public void Symbol_fact_projections_carry_every_field_they_map()
    {
        var symbol = FullyPopulatedSymbol();

        var methodRef = SymbolFactProjections.ToMethodRef(symbol);
        AssertNoDefaults(methodRef, exempt: []);
        methodRef.ContainingTypeId.ShouldBe("T:Ns.Type");

        AssertNoDefaults(SymbolFactProjections.ToMethodSymbol(symbol), exempt: []);
        SymbolFactProjections.ToMethodSymbol(symbol).Signature.ShouldBe("public abstract void Method(int a)");

        var typeSymbol = SymbolFactProjections.ToTypeSymbol(symbol);
        AssertNoDefaults(typeSymbol, exempt: []);
        typeSymbol.IsAbstract.ShouldBeTrue();
        // …and the token test is a TOKEN test, not a substring one.
        SymbolFactProjections.ToTypeSymbol(symbol with { Modifiers = "public abstractly" }).IsAbstract.ShouldBeFalse();

        var meta = SymbolFactProjections.ToMethodMeta(symbol);
        AssertNoDefaults(meta, exempt: []);
        meta.IsGenerated.ShouldBeTrue();
        SymbolFactProjections.ToMethodMeta(symbol with { FilePath = @"C:\src\Type.cs" }).IsGenerated.ShouldBeFalse();
    }

    // The bounded MethodRef path indexes its reader by `(int)MethodRefColumn.X` and generates its SELECT list
    // from the same enum, so the enum members must BE columns of symbol_facts (ReferenceFactEntity's sibling
    // mirrors the table 1:1) and properties of SymbolFact. A rename on either side must fail here rather than
    // producing a "no such column" at query time on a user's store.
    [Test]
    public void Method_ref_column_names_are_real_symbol_fact_columns_and_properties()
    {
        var entityProperties = typeof(SymbolFactEntity).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var recordProperties = typeof(SymbolFact).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        SymbolFactProjections.MethodRefColumns.ShouldNotBeEmpty();
        foreach (var column in SymbolFactProjections.MethodRefColumns)
        {
            entityProperties.ShouldContain(column, $"MethodRefColumn.{column} is not a symbol_facts column");
            recordProperties.ShouldContain(column, $"MethodRefColumn.{column} is not a SymbolFact property");
        }

        // Declaration order IS the ordinal set, so the generated SELECT list must be in that order.
        SymbolFactRowsSelectList().ShouldBe(string.Join(", ", SymbolFactProjections.MethodRefColumns.Select(c => $"s.{c}")));
    }

    // The copy with no parity gate before this change: the raw-ADO BOUNDED MethodRef reader vs the EF
    // whole-store loader, compared field by field on a real playground store (the shape ReachInputProjectionTests
    // uses for invocations). The bounded loader returns the methods inside the reach closure, so the whole-store
    // set restricted to those ids is exactly what it must return.
    [Test]
    public async Task Bounded_method_refs_are_field_equal_to_the_whole_store_loader()
    {
        var playground = await playgrounds.LegacyNet48Async();
        var rules = RuleSetLoader.Load(playground.WorkingDirectory);

        var directory = Path.Combine(Path.GetTempPath(), "rig-projshare-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "rig.db");
        try
        {
            await using (var write = new RigDbContext(databasePath, pooling: false))
            {
                await Writes.SaveAsync(write, playground.Result);
            }

            await using (var build = new RigDbContext(databasePath, pooling: false))
            {
                await GraphMaterializer.BuildAsync(build, rules.Handoff.ToArray(), factoryRules: rules.Factory);
            }

            await using var read = new RigDbContext(databasePath, pooling: false);
            var wholeStore = (await Reads.LoadFactGraphAsync(read, rules.Handoff, rules.Redirect)).Methods;
            wholeStore.ShouldNotBeEmpty();

            var compared = 0;
            foreach (var pattern in new[] { "LegacyNet48Web", "SchedulerZoo", "LockZoo" })
            {
                var bounded = (await SqlReachability.LoadBoundedGraphAsync(read, pattern, SqlReachability.Direction.Forward)).Methods;
                var ids = bounded.Select(m => m.SymbolId).ToHashSet(StringComparer.Ordinal);
                var expected = wholeStore.Where(m => ids.Contains(m.SymbolId)).ToList();

                Rendered(bounded).ShouldBe(Rendered(expected), customMessage: $"bounded != whole-store MethodRef records for '{pattern}'");
                bounded.ShouldNotBeEmpty($"pattern '{pattern}' bounded no methods — the comparison would be vacuous");
                // ANTI-VACUITY on the two fields most likely to be lost to an ordinal slip: an all-null
                // ContainingTypeId / all-false IsOverride set would compare equal against a broken reader.
                compared += bounded.Count;
            }

            compared.ShouldBeGreaterThan(0);
            wholeStore.Count(m => m.ContainingTypeId is not null).ShouldBeGreaterThan(0);
            wholeStore.Count(m => m.IsOverride).ShouldBeGreaterThan(0);
            wholeStore.Count(m => m.Line > 0).ShouldBeGreaterThan(0);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            { /* best-effort cleanup */
            }
        }
    }

    // SymbolFactRows.MethodRefSelectList is internal to Rig.Storage; the test project sees it, but route it
    // through one helper so the intent (generated, not hand-written) reads clearly above.
    private static string SymbolFactRowsSelectList() => SymbolFactRows.MethodRefSelectList("s");

    // Every public property of the record must be non-default, except the named exemptions. Reflection so a
    // field ADDED to the record is covered here automatically — the whole point of the gate.
    private static void AssertNoDefaults<T>(T record, string[] exempt)
    {
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        properties.ShouldNotBeEmpty();
        foreach (var property in properties)
        {
            if (exempt.Contains(property.Name, StringComparer.Ordinal))
            {
                property.GetValue(record).ShouldBe(null, $"{typeof(T).Name}.{property.Name} is documented as always-null");
                continue;
            }

            IsDefaultValue(property.GetValue(record))
                .ShouldBeFalse(
                    $"{typeof(T).Name}.{property.Name} came back at its default — the projection dropped it "
                        + "(or a new field needs mapping in the shared projection)."
                );
        }
    }

    private static bool IsDefaultValue(object? value) =>
        value switch
        {
            null => true,
            string s => s.Length == 0,
            bool b => !b,
            int i => i == 0,
            _ => false,
        };

    // Every public property, sorted — neither loader promises an order (mirrors ReachInputProjectionTests).
    private static string[] Rendered<T>(IEnumerable<T> records)
    {
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        properties.ShouldNotBeEmpty();
        return records
            .Select(record => string.Join('|', properties.Select(p => $"{p.Name}={p.GetValue(record) ?? "<null>"}")))
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToArray();
    }
}
