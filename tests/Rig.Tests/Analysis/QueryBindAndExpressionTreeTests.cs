using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rig.Analysis;
using Rig.Analysis.Extraction;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Analysis;

// The two v5 facts and their derive-side consumers.
//
// 1. EnclosingLoopBindType — query syntax compiles to Select/SelectMany/Where binds, and the DECLARING TYPE of
//    the bound method is the only signal that separates `from x in xs` (System.Linq.Enumerable — a loop) from
//    `from x in validation` (the monad's own extension class — a single-shot bind). The IterationContext gate
//    runs it through the SAME enumeratingMethods allow-list that keeps Option.Map out of lambda contexts.
//    Calibration ground truth (MedDBase, 2026-08-03): monadic comprehensions (Validation/Either/first-party
//    Tal) were ~54% of all cross-method-N+1 false positives; a deny-list of known monads was rejected because
//    Tal proves the monad set is open — only the enumerating ALLOW-list closes it.
//
// 2. InExpressionTree — a reference inside QUOTED code (an Expression<> lambda, an IQueryable clause) never
//    executes as C#: a nav-property getter in `where p.Nav.X == y` is a SQL join, not a call. Such references
//    derive no invocation effect and anchor no iteration fanout. Constructor effects are deliberately NOT
//    gated (a `new Dto(...)` in a select projection executes per row at materialization).
public sealed class QueryBindAndExpressionTreeTests
{
    private static FactExtractionResult Extract(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "Snippet.cs");
        var compilation = CSharpCompilation.Create(
            "Snippet",
            [tree],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Queryable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(IQueryable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Expressions.Expression).Assembly.Location),
                MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        var model = compilation.GetSemanticModel(tree);
        return FactExtractor.Extract(new SourceModel("Snippet", "Snippet.cs", tree, tree.GetRoot(), model), new SymbolStringCache());
    }

    private const string MonadFixture = """
        using System;
        using System.Collections.Generic;
        using System.Linq;

        namespace App
        {
            // A minimal single-value monad with query-syntax support — the LanguageExt.Validation / first-party
            // Tal shape. Its binds declare on ValidationExt, NOT on an enumerating type.
            public readonly struct Validation<T>
            {
                public T Value { get; init; }
            }

            public static class ValidationExt
            {
                public static Validation<R> Select<T, R>(this Validation<T> v, Func<T, R> map) => new() { Value = map(v.Value) };

                public static Validation<R> SelectMany<T, U, R>(
                    this Validation<T> v,
                    Func<T, Validation<U>> bind,
                    Func<T, U, R> project
                ) => new() { Value = project(v.Value, bind(v.Value).Value) };
            }

            public static class Db
            {
                public static int Fetch(int key) => key;
            }

            public sealed class Caller
            {
                public int Monadic(Validation<int> a)
                {
                    var result = from x in a from y in a select Db.Fetch(x) + y;
                    return result.Value;
                }

                public List<int> Collection(List<int> ids)
                {
                    return (from id in ids select Db.Fetch(id)).ToList();
                }
            }
        }
        """;

    [Test]
    public void Query_over_a_monad_records_the_monads_bind_declaring_type()
    {
        var result = Extract(MonadFixture);

        var monadic = result.References.Single(r =>
            r.RefKind == "invocation" && r.TargetSymbolId.Contains("Db.Fetch") && r.EnclosingSymbolId!.Contains("Monadic")
        );
        monadic.EnclosingLoopKind.ShouldBe("query");
        monadic.EnclosingLoopBindType.ShouldBe("App.ValidationExt");
        monadic.InExpressionTree.ShouldBeFalse();
    }

    [Test]
    public void Query_over_a_collection_records_the_enumerable_bind_declaring_type()
    {
        var result = Extract(MonadFixture);

        var collection = result.References.Single(r =>
            r.RefKind == "invocation" && r.TargetSymbolId.Contains("Db.Fetch") && r.EnclosingSymbolId!.Contains("Collection")
        );
        collection.EnclosingLoopKind.ShouldBe("query");
        collection.EnclosingLoopBindType.ShouldBe("System.Linq.Enumerable");
        collection.InExpressionTree.ShouldBeFalse();
    }

    private const string QuotedFixture = """
        using System;
        using System.Linq;
        using System.Linq.Expressions;

        namespace App
        {
            public sealed class Person
            {
                public int Key { get; set; }
            }

            public static class Db
            {
                public static int Fetch(int key) => key;
            }

            public sealed class Caller
            {
                public IQueryable<Person> Quoted(IQueryable<Person> people)
                {
                    return from p in people where Db.Fetch(p.Key) > 0 select p;
                }

                public Expression<Func<int, int>> QuotedLambda()
                {
                    return x => Db.Fetch(x);
                }

                public Func<int, int> ExecutableLambda()
                {
                    return x => Db.Fetch(x);
                }
            }
        }
        """;

    [Test]
    public void A_call_in_an_IQueryable_clause_is_quoted()
    {
        var result = Extract(QuotedFixture);

        var quoted = result.References.Single(r =>
            r.RefKind == "invocation" && r.TargetSymbolId.Contains("Db.Fetch") && r.EnclosingSymbolId!.Contains("Quoted(")
        );
        quoted.InExpressionTree.ShouldBeTrue();
        quoted.EnclosingLoopKind.ShouldBe("query");
        quoted.EnclosingLoopBindType.ShouldBe("System.Linq.Queryable");
    }

    [Test]
    public void A_call_in_an_Expression_lambda_is_quoted_and_in_a_delegate_lambda_is_not()
    {
        var result = Extract(QuotedFixture);

        var quotedLambda = result.References.Single(r =>
            r.RefKind == "invocation" && r.TargetSymbolId.Contains("Db.Fetch") && r.EnclosingSymbolId!.Contains("QuotedLambda")
        );
        quotedLambda.InExpressionTree.ShouldBeTrue();

        var executable = result.References.Single(r =>
            r.RefKind == "invocation" && r.TargetSymbolId.Contains("Db.Fetch") && r.EnclosingSymbolId!.Contains("ExecutableLambda")
        );
        executable.InExpressionTree.ShouldBeFalse();
    }

    // --- Derive-side: the IterationContext enumerating gate over the bind type. ---

    private static FactObservationRules Rules(params FactEnumeratingMethodRule[] enumerating) =>
        new(
            ResilienceRetry: [],
            ConcurrencyHandled: [],
            ParallelFanout: [],
            ResourceSpan: [],
            SerializationHazard: [],
            NPlusOne: [],
            EnumeratingMethods: enumerating
        );

    private static readonly FactEnumeratingMethodRule LinqOnly = new(
        Methods: ["Select", "SelectMany", "Where"],
        DeclaringTypes: ["System.Linq.Enumerable"]
    );

    [Test]
    public void A_query_binding_onto_a_non_enumerating_type_is_not_an_iteration_context()
    {
        var context = IterationContext.Of(
            loopKind: "query",
            loopDetail: "x, y in a",
            enclosingInvocations: [],
            rules: Rules(LinqOnly),
            loopElementType: "App.Validation<int>",
            loopBindType: "App.ValidationExt"
        );

        context.Kind.ShouldBeNull();
        context.Identifiers.ShouldBeEmpty();
    }

    [Test]
    public void A_query_binding_onto_an_enumerating_type_stays_a_loop()
    {
        var context = IterationContext.Of(
            loopKind: "query",
            loopDetail: "id in ids",
            enclosingInvocations: [],
            rules: Rules(LinqOnly),
            loopElementType: "int",
            loopBindType: "System.Linq.Enumerable"
        );

        context.Kind.ShouldBe("query");
        context.Identifiers.ShouldBe(["id"]);
    }

    [Test]
    public void An_unresolved_bind_type_fails_open_and_a_foreach_is_never_gated()
    {
        IterationContext.Of("query", "id in ids", [], Rules(LinqOnly), loopBindType: null).Kind.ShouldBe("query");
        IterationContext.Of("foreach", "id in ids", [], Rules(LinqOnly), loopBindType: "App.ValidationExt").Kind.ShouldBe("foreach");
    }

    [Test]
    public void A_gated_query_falls_back_to_an_enclosing_enumerating_lambda()
    {
        // `ids.Select(id => from v in validation select Fetch(v))` — the monadic query is not iteration, but
        // the enumerating Select around it is, and its parameter is the per-element identifier.
        var context = IterationContext.Of(
            loopKind: "query",
            loopDetail: "v in validation",
            enclosingInvocations:
            [
                new FactStructuralContext.EnclosingInvocation(
                    ReceiverText: "ids",
                    ReceiverType: "System.Collections.Generic.List<int>",
                    MethodName: "Select",
                    DeclaringType: "System.Linq.Enumerable",
                    LambdaParameter: "id",
                    LambdaParameterType: "int"
                ),
            ],
            rules: Rules(LinqOnly),
            loopBindType: "App.ValidationExt"
        );

        context.Kind.ShouldBe("lambda");
        context.Identifiers.ShouldBe(["id"]);
    }

    // --- Derive-side: quoted references derive no effect and anchor no fanout. ---

    [Test]
    public void A_quoted_invocation_derives_no_effect_and_anchors_no_iteration_fanout()
    {
        var quoted = new FactInvocation(
            Target: "M:App.Db.Fetch(System.Int32)",
            Enclosing: "M:App.Caller.Quoted",
            FilePath: "Snippet.cs",
            Line: 10,
            Loop: new FactLoopContext(Kind: "query", Detail: "p in people"),
            InExpressionTree: true
        );
        var live = quoted with { InExpressionTree = false, Line = 20 };
        var rules = new[]
        {
            new FactEffectRule(
                Provider: "db",
                Operation: "read",
                Methods: ["Fetch"],
                DeclaringTypes: [],
                ReceiverTypes: [],
                Resource: "declaring_type"
            ),
        };

        var effects = FactEffectDeriver.Derive([quoted, live], rules);
        effects.ShouldHaveSingleItem().Line.ShouldBe(20);

        var fanouts = FactIterationFanoutDeriver.Derive([quoted, live], Rules(LinqOnly));
        fanouts.ShouldHaveSingleItem().Event.Line.ShouldBe(20);
    }
}
