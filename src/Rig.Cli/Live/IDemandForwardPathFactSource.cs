using Rig.Domain.Data;
using Rig.Domain.Functions;

namespace Rig.Cli.Live;

internal sealed record DemandForwardReachInputs(DemandForwardGraphResult Demand, Rig.Storage.Queries.SqlReachability.ReachInputs Inputs);

// Optional live-only capability. StoreQueryFactSource intentionally does not implement it: the store keeps
// its existing SQL-bounded graph loader and IQueryFactSource stays small and honest.
internal interface IDemandForwardPathFactSource
{
    Task<DemandForwardGraphResult> LoadDemandForwardPathGraphAsync(
        string fromPattern,
        RuleSet shapedRules,
        int maxDepth,
        FactPathFinder.TraversalMode mode,
        bool classifyEventSubscriptions,
        int? maxNodes = null,
        int? maxGenericWork = null
    );

    Task<DemandForwardReachInputs> LoadDemandForwardReachInputsAsync(
        string fromPattern,
        RuleSet shapedRules,
        int maxDepth,
        FactPathFinder.TraversalMode mode,
        bool classifyEventSubscriptions,
        int? maxNodes = null,
        int? maxGenericWork = null
    );
}
