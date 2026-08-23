using Rig.Domain.Functions;

namespace Rig.Cli.Live;

// Optional live-only capability for callers' keyed reverse graph. The durable store intentionally keeps
// its existing SQL-bounded loader, while resident snapshots can answer from keyed fact partitions without
// materializing the whole traversal graph.
internal interface IDemandReverseCallersFactSource
{
    Task<DemandReverseCallersGraphResult> LoadDemandReverseCallersGraphAsync(
        DemandForwardGraphRules rules,
        DemandReverseCallersGraphRequest request
    );
}
