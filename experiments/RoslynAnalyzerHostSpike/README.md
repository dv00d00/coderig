# Roslyn analyzer host reachability spike

Throwaway experiment. It answers one question:

> Starting from a method in the analyzer's `Compilation`, which downstream method bodies can a real
> `DiagnosticAnalyzer` inspect before the call chain becomes metadata-only or requires whole-program dispatch?

The effect classifier is deliberately trivial: a method carrying `[SpikeEffect]` is a direct effect. This keeps
host visibility separate from rig's production effect rules.

Run:

```bash
dotnet run --project experiments/RoslynAnalyzerHostSpike
```

The program exercises six shapes and prints one TSV row per root:

1. multiple syntax trees in one compilation;
2. two live `CompilationReference` hops;
3. a live source hop calling a direct effect whose symbol is PE metadata;
4. a live source hop calling an unmarked PE metadata method whose body contains the effect;
5. a PE metadata project reference at the first hop;
6. an interface call whose implementation exists in the same compilation.

The distinction between 3 and 4 is load-bearing: a direct effect can be classified from the metadata symbol
at its call site, but a transitive effect hidden inside a metadata-only body requires that body to be available.

## Observed result

```text
scenario                            effects  max_depth  unresolved_boundaries
same_compilation                    1        2          -
two_source_reference_hops           1        2          -
source_calls_direct_metadata_effect 1        2          -
source_then_hidden_metadata_body    0        -          metadata:HiddenMiddle.Run
metadata_first_hop                  0        -          metadata:Middle.Run
interface_dispatch                  0        -          dispatch:IWorker.Run
```

This establishes a narrow best-case result, not a product architecture:

- method bodies in the current compilation can be followed;
- method bodies behind explicitly supplied live `CompilationReference` instances can also be followed across
  multiple hops;
- PE metadata preserves enough symbol information to classify a direct effect at a call site, but not to inspect
  the callee body;
- ordinary semantic binding stops at the interface member. Discovering and following possible implementations is
  a separate whole-program call-graph operation;
- Roslyn emits `RS1030` for the experiment's cross-tree `Compilation.GetSemanticModel()` calls: even the successful
  live-source traversal is not an analyzer pattern Roslyn endorses.

The harness constructs its `CompilationReference` graph in memory. It does not claim that an IDE, command-line
build, or NuGet analyzer host supplies project references in that form. Host parity is deliberately outside this
core experiment.

No production project references this experiment.
