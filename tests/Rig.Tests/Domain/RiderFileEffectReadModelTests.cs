using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

public sealed class RiderFileEffectReadModelTests
{
    private const string File = "/repo/Effectful.cs";
    private const string CleanFile = "/repo/Clean.cs";
    private const string OwnersFile = "/repo/Owners.cs";

    [Test]
    public void Projects_one_union_closure_into_semantic_models_for_every_indexed_file()
    {
        var graph = Graph(
            [
                new CallEdge("M:File.Zed", "M:ReadOwner", "invocation", File, 10),
                new CallEdge("M:File.Alpha", "M:Bridge", "invocation", File, 20),
                new CallEdge("M:Bridge", "M:WriteOwner", "invocation", OwnersFile, 30),
                new CallEdge("M:File.Ignored", "M:HttpOwner", "invocation", File, 40),
            ],
            "M:File.Clean"
        );
        var symbols = new[]
        {
            Method("M:File.Zed", File, 10),
            Method("M:File.Alpha", File, 20),
            Method("M:File.Alpha", File, 90), // canonical duplicate must not produce a second row
            Method("M:File.Ignored", File, 40),
            Method("M:File.Clean", CleanFile, 5),
            Method("M:Bridge", OwnersFile, 30),
            Method("M:ReadOwner", OwnersFile, 50),
            Method("M:WriteOwner", OwnersFile, 60),
            Method("M:HttpOwner", OwnersFile, 70),
            Lambda("M:File.Alpha~lambda0", File, 21),
        };
        var effects = new[]
        {
            Effect("ado", "read", "M:ReadOwner", 50),
            Effect("ef", "write", "M:WriteOwner", 60),
            Effect("http", "send", "M:HttpOwner", 70),
            Effect("ADO", "read", "M:HttpOwner", 71), // matching is ordinal
        };
        var selector = new FileEffectSelector("sql", [new EffectPredicate("ado", "read"), new EffectPredicate("ef")]);

        var index = FileEffectReadModelIndex.Build(
            graph,
            symbols,
            effects,
            selector,
            indexedFilePaths: [File, CleanFile, OwnersFile, "/repo/NoMethods.cs"]
        );

        var file = index.Find(File);
        file.ShouldNotBeNull();
        file!.EffectSelectors.ShouldBe(["sql"]);
        file.Methods.Select(method =>
                (method.SymbolId, Family: method.Effects.Single().Family, NearestDepth: method.Effects.Single().NearestDepth)
            )
            .ShouldBe([("M:File.Alpha", "sql", 2), ("M:File.Zed", "sql", 1)]);
        file.CallSites.Select(callSite =>
                (
                    callSite.EnclosingSymbolId,
                    callSite.TargetSymbolId,
                    callSite.Line,
                    Family: callSite.Effects.Single().Family,
                    NearestDepth: callSite.Effects.Single().NearestDepth
                )
            )
            .ShouldBe([("M:File.Alpha", "M:Bridge", 20, "sql", 1), ("M:File.Zed", "M:ReadOwner", 10, "sql", 0)]);
        index.Find(File).ShouldBeSameAs(file);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            index.Find("/REPO/EFFECTFUL.CS").ShouldBeSameAs(file);
        }
        else
        {
            index.Find("/REPO/EFFECTFUL.CS").ShouldBeNull();
        }

        var clean = index.Find(CleanFile);
        clean.ShouldNotBeNull();
        clean!.Methods.ShouldBeEmpty();
        clean.CallSites.ShouldBeEmpty();
        index.Find(CleanFile).ShouldBeSameAs(clean);

        index.Find("/repo/NoMethods.cs").ShouldNotBeNull().Methods.ShouldBeEmpty();

        index.Find("/repo/Unknown.cs").ShouldBeNull();

        var contractProperties = typeof(FileEffectMethod).GetProperties().Select(property => property.Name).ToArray();
        contractProperties.ShouldBe([nameof(FileEffectMethod.SymbolId), nameof(FileEffectMethod.Effects)]);
        typeof(FileEffectCallSite)
            .GetProperties()
            .Select(property => property.Name)
            .ShouldBe([
                nameof(FileEffectCallSite.EnclosingSymbolId),
                nameof(FileEffectCallSite.TargetSymbolId),
                nameof(FileEffectCallSite.Line),
                nameof(FileEffectCallSite.Effects),
            ]);
    }

    // A TARGET is recovered only when the site holds exactly one invocation edge — `Use(Read(), Other())`
    // shares one line, so naming one of them would be a false positive. The ambiguous line still gets a row,
    // just with an EMPTY target: "an effect is here, no resolvable callee to name".
    [Test]
    public void Direct_effect_call_site_is_projected_only_when_its_target_is_unambiguous()
    {
        var graph = Graph([
            new CallEdge("M:File.Direct", "M:Db.Execute", "invocation", File, 10),
            new CallEdge("M:File.Ambiguous", "M:Db.Execute", "invocation", File, 20),
            new CallEdge("M:File.Ambiguous", "M:Log.Write", "invocation", File, 20),
        ]);
        var symbols = new[] { Method("M:File.Direct", File, 9), Method("M:File.Ambiguous", File, 19) };
        var effects = new[]
        {
            new DerivedEffect("ado", "read", "db", "M:File.Direct", File, 10),
            new DerivedEffect("ado", "read", "db", "M:File.Ambiguous", File, 20),
        };

        var model = FileEffectReadModelIndex
            .Build(graph, symbols, effects, new FileEffectSelector("sql", [new EffectPredicate("ado")]))
            .Find(File)
            .ShouldNotBeNull();

        model
            .CallSites.Select(site => (site.EnclosingSymbolId, site.TargetSymbolId, site.Line))
            .ShouldBe([("M:File.Ambiguous", "", 20), ("M:File.Direct", "M:Db.Execute", 10)]);
    }

    // A call into EXTERNAL code produces neither a CallEdge nor a node, so there was nothing to join and the
    // line went unmarked — live proof: Writes.SaveFactsBatchedAsync's DbConnection.BeginTransactionAsync
    // (338) and DbTransaction.CommitAsync (499) both report `d0 db_transaction` yet had no call-site row.
    // The effect fact's own FilePath+Line is the anchor; the target is empty because no in-solution symbol
    // exists to name, and the depth is 0 because the effect is right there.
    [Test]
    public void An_effect_at_a_call_into_external_code_is_projected_with_an_empty_target_at_depth_zero()
    {
        var graph = Graph([new CallEdge("M:File.Save", "M:File.Pure", "invocation", File, 12)]);
        var symbols = new[] { Method("M:File.Save", File, 9), Method("M:File.Pure", File, 60) };
        var effects = new[]
        {
            new DerivedEffect("db_transaction", "begin", "Common.DbConnection", "M:File.Save", File, 338),
            new DerivedEffect("db_transaction", "commit", "Common.DbTransaction", "M:File.Save", File, 499),
            // No mined position: there is no line to anchor a mark to, so no row.
            new DerivedEffect("db_transaction", "rollback", "Common.DbTransaction", "M:File.Save", File, 0),
        };

        var model = FileEffectReadModelIndex
            .Build(graph, symbols, effects, new FileEffectSelector("sql", [new EffectPredicate("db_transaction")]))
            .Find(File)
            .ShouldNotBeNull();

        model.Methods.Select(method => (method.SymbolId, method.Effects.Single().NearestDepth)).ShouldBe([("M:File.Save", 0)]);
        model
            .CallSites.Select(site =>
                (site.EnclosingSymbolId, site.TargetSymbolId, site.Line, site.Effects.Single().Family, site.Effects.Single().NearestDepth)
            )
            .ShouldBe([("M:File.Save", "", 338, "sql", 0), ("M:File.Save", "", 499, "sql", 0)]);
    }

    // Both arms can claim one (enclosing, line). The edge-derived row wins because it names a target Rider
    // can resolve against the PSI invocation; two rows on one line would double-mark it.
    [Test]
    public void An_edge_derived_call_site_wins_over_an_effect_derived_one_on_the_same_line()
    {
        var graph = Graph([
            new CallEdge("M:File.Caller", "M:Owner", "invocation", File, 10),
            new CallEdge("M:File.Caller", "M:Bridge", "invocation", File, 20),
            new CallEdge("M:Bridge", "M:Owner", "invocation", OwnersFile, 25),
        ]);
        var symbols = new[] { Method("M:File.Caller", File, 9), Method("M:Bridge", OwnersFile, 24), Method("M:Owner", OwnersFile, 30) };
        var effects = new[]
        {
            Effect("ado", "read", "M:Owner", 31),
            // Same line as the single-edge site: the recovered target must survive, the empty one must not.
            new DerivedEffect("ado", "read", "db", "M:File.Caller", File, 10),
            // Same line as an INDIRECT site: the reachable callee still wins over the empty target.
            new DerivedEffect("ado", "read", "db", "M:File.Caller", File, 20),
        };

        var model = FileEffectReadModelIndex
            .Build(graph, symbols, effects, new FileEffectSelector("sql", [new EffectPredicate("ado")]))
            .Find(File)
            .ShouldNotBeNull();

        model
            .CallSites.Select(site => (site.EnclosingSymbolId, site.TargetSymbolId, site.Line, site.Effects.Single().NearestDepth))
            .ShouldBe([("M:File.Caller", "M:Owner", 10, 0), ("M:File.Caller", "M:Bridge", 20, 1)]);
    }

    // The effect-derived arm keys off the effect's OWN FilePath, so an effect in the callee's body belongs to
    // the callee's file. Projecting it into the caller's file would put a mark on an unrelated line number.
    [Test]
    public void An_effect_in_another_file_is_not_projected_as_a_call_site_of_this_file()
    {
        var graph = Graph([new CallEdge("M:File.Caller", "M:Owner", "invocation", File, 10)]);
        var symbols = new[] { Method("M:File.Caller", File, 9), Method("M:Owner", OwnersFile, 30) };
        var effects = new[]
        {
            // Lives in the callee's own body/file.
            Effect("ado", "read", "M:Owner", 31),
            // Physically in THIS file, but enclosed by a method this file does not declare (a partial-class
            // sibling or a `~mono` clone body): still not a call site of this file.
            new DerivedEffect("ado", "read", "db", "M:Owner", File, 77),
        };

        var index = FileEffectReadModelIndex.Build(graph, symbols, effects, new FileEffectSelector("sql", [new EffectPredicate("ado")]));

        index
            .Find(File)
            .ShouldNotBeNull()
            .CallSites.Select(site => (site.EnclosingSymbolId, site.TargetSymbolId, site.Line))
            .ShouldBe([("M:File.Caller", "M:Owner", 10)]);
        // The effect-derived row lands in the file the effect is actually in.
        index
            .Find(OwnersFile)
            .ShouldNotBeNull()
            .CallSites.Select(site => (site.EnclosingSymbolId, site.TargetSymbolId, site.Line))
            .ShouldBe([("M:Owner", "", 31)]);
    }

    // The tie case that a strict `calleeDepth < callerDepth` test dropped: one body calls two effectful
    // methods, the first hands the caller its nearest distance, and the sibling call ties it. Both are real
    // ways into the family, so both must be projected.
    [Test]
    public void A_second_effectful_call_from_one_body_is_projected_even_when_it_does_not_shorten_the_distance()
    {
        var graph = Graph([
            new CallEdge("M:File.Caller", "M:NearOwner", "invocation", File, 10),
            new CallEdge("M:File.Caller", "M:Sibling", "invocation", File, 11),
            new CallEdge("M:Sibling", "M:FarOwner", "invocation", OwnersFile, 30),
        ]);
        var symbols = new[]
        {
            Method("M:File.Caller", File, 9),
            Method("M:Sibling", OwnersFile, 20),
            Method("M:NearOwner", OwnersFile, 30),
            Method("M:FarOwner", OwnersFile, 40),
        };
        var effects = new[] { Effect("ado", "read", "M:NearOwner", 30), Effect("ado", "read", "M:FarOwner", 40) };

        var model = FileEffectReadModelIndex
            .Build(graph, symbols, effects, new FileEffectSelector("sql", [new EffectPredicate("ado")]))
            .Find(File)
            .ShouldNotBeNull();

        model
            .CallSites.Select(site => (site.EnclosingSymbolId, site.TargetSymbolId, site.Line, site.Effects.Single().NearestDepth))
            .ShouldBe([("M:File.Caller", "M:NearOwner", 10, 0), ("M:File.Caller", "M:Sibling", 11, 1)]);
    }

    // Static monomorphization REDIRECTS a concrete generic call to a `{baseId}~mono⟨binding⟩` node
    // (FactPathFinder.IsExactNodeMatch documents the redirect), while the reverse closure is seeded from
    // effect owners, which are always BASE ids. Comparing raw ids therefore dropped every generic call site:
    // Writes.SaveFactsBatchedAsync calls InsertRows``1 five times, and all five went unmarked in Rider.
    [Test]
    public void A_generic_call_site_is_projected_through_its_monomorphized_node_id()
    {
        var baseId = "M:Owners.Insert``1(System.String,``0)";
        var monoId = MonomorphizedNodeId.For(baseId, [], ["System.String"]);
        var graph = Graph([new CallEdge("M:File.Caller", monoId, "invocation", File, 10)], baseId);
        var symbols = new[] { Method("M:File.Caller", File, 9), Method(baseId, OwnersFile, 30) };
        var effects = new[] { Effect("ado", "read", baseId, 31) };

        var model = FileEffectReadModelIndex
            .Build(graph, symbols, effects, new FileEffectSelector("sql", [new EffectPredicate("ado")]))
            .Find(File)
            .ShouldNotBeNull();

        // The TARGET is reported as the base id: Rider resolves it against a PSI declaration, which never
        // carries a monomorphized binding.
        model
            .CallSites.Select(site => (site.EnclosingSymbolId, site.TargetSymbolId, site.Line, site.Effects.Single().NearestDepth))
            .ShouldBe([("M:File.Caller", baseId, 10, 0)]);
    }

    // The same normalisation, one layer up: when the ONLY way from a method to the family runs through a
    // monomorphized node, the method summary itself went missing — so a caller of a generic repository lost
    // its Code Vision line, not just its call-site marks.
    [Test]
    public void A_method_reaching_the_family_only_through_a_monomorphized_node_keeps_its_summary()
    {
        var midId = "M:Owners.Mid``1(``0)";
        var ownerId = "M:Owners.Owner()";
        var graph = Graph([
            new CallEdge(MonomorphizedNodeId.For(midId, [], ["System.String"]), ownerId, "invocation", OwnersFile, 40),
            new CallEdge("M:File.Caller", MonomorphizedNodeId.For(midId, [], ["System.String"]), "invocation", File, 10),
        ]);
        var symbols = new[] { Method("M:File.Caller", File, 9), Method(midId, OwnersFile, 39), Method(ownerId, OwnersFile, 45) };
        var effects = new[] { Effect("ado", "read", ownerId, 46) };

        var index = FileEffectReadModelIndex.Build(graph, symbols, effects, new FileEffectSelector("sql", [new EffectPredicate("ado")]));

        index
            .Find(OwnersFile)
            .ShouldNotBeNull()
            .Methods.Select(method => (method.SymbolId, method.Effects.Single().NearestDepth))
            .ShouldBe([(midId, 1), (ownerId, 0)]);
        index
            .Find(File)
            .ShouldNotBeNull()
            .Methods.Select(method => (method.SymbolId, method.Effects.Single().NearestDepth))
            .ShouldBe([("M:File.Caller", 2)]);
    }

    // Before the call line was carried, the projection deduplicated on (enclosing, target) alone, so a body
    // calling one effectful method twice produced a SINGLE row and a consumer had to re-derive the positions
    // itself. Two lines, two rows; the same line twice still collapses, because extraction mines no column.
    [Test]
    public void Repeated_calls_to_one_target_are_projected_once_per_line()
    {
        var graph = Graph([
            new CallEdge("M:File.Caller", "M:Owner", "invocation", File, 10),
            new CallEdge("M:File.Caller", "M:Owner", "invocation", File, 14),
            new CallEdge("M:File.Caller", "M:Owner", "invocation", File, 14),
        ]);
        var symbols = new[] { Method("M:File.Caller", File, 9), Method("M:Owner", OwnersFile, 30) };
        var effects = new[] { Effect("ado", "read", "M:Owner", 31) };

        var model = FileEffectReadModelIndex
            .Build(graph, symbols, effects, new FileEffectSelector("sql", [new EffectPredicate("ado")]))
            .Find(File)
            .ShouldNotBeNull();

        model.CallSites.Select(site => (site.TargetSymbolId, site.Line)).ShouldBe([("M:Owner", 10), ("M:Owner", 14)]);
    }

    [Test]
    public void Selector_requires_a_named_family_and_at_least_one_valid_predicate()
    {
        Should.Throw<ArgumentException>(() => new FileEffectSelector(" ", [new EffectPredicate("ado")])).ParamName.ShouldBe("family");
        Should.Throw<ArgumentException>(() => new FileEffectSelector("sql", [])).ParamName.ShouldBe("predicates");
        Should.Throw<ArgumentException>(() => new FileEffectSelector("sql", [new EffectPredicate(" ")])).ParamName.ShouldBe("predicates");
    }

    private static DerivedEffect Effect(string provider, string operation, string owner, int line) =>
        new(provider, operation, "db", owner, OwnersFile, line);

    private static SymbolFact Method(string id, string file, int line) =>
        new(
            id,
            SymbolKinds.Method,
            id,
            "Fixture",
            "T:Fixture",
            "",
            "",
            $"void {id}()",
            file,
            line,
            line + 1,
            "Fixture",
            false,
            BodyHash: id
        );

    private static SymbolFact Lambda(string id, string file, int line) =>
        new(id, "lambda", "lambda", "Fixture", "M:File.Alpha", "", "", "lambda", file, line, line, "Fixture", false, BodyHash: id);

    private static FactGraphData Graph(IReadOnlyList<CallEdge> edges, params string[] extraMethods)
    {
        var methods = edges
            .SelectMany(edge => new[] { edge.Caller, edge.Callee })
            .Concat(extraMethods)
            .Distinct(StringComparer.Ordinal)
            .Select(id => new MethodRef(id, id, "T:Fixture"))
            .ToArray();
        return new FactGraphData(edges, Array.Empty<ImplementsEdge>(), methods, Array.Empty<BaseEdge>());
    }
}
