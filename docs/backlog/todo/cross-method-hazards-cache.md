# /api/hazards recomputes the cross-method correlation per request

**Status:** todo
**Triage:** ready-for-agent

HazardsService now folds tier-3 cross-method amplification anchors into the mark stream, but unlike
hazard effects (LoadOrDeriveHazardEffectsAsync) and graph findings, the cross-method computation —
LoadInvocationRefsAsync (~2.4M rows on MedDBase) + shaped graph + the presence correlation — runs
UNCACHED on every /api/hazards request (~30–60s on the MedDBase store; observed live 2026-08-04).

Fix: cache the AnchorFinding list in cache.db keyed by (storeKey, rulesHash) exactly like the hazard
effects, with a FindingViewSchema-style version token so a derivation change invalidates. Derive and
serve then share the entry. Also consider sharing the shaped graph load already performed by
TreeQueryService within the same request.
