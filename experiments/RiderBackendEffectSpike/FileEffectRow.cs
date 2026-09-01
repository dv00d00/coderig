using System.Collections.Generic;

namespace CodeRig.Rider;

internal sealed class FileEffectRow
{
    public FileEffectRow(string symbolDocId, string family, int nearestDepth)
    {
        SymbolDocId = symbolDocId;
        Family = family;
        NearestDepth = nearestDepth;
    }

    public string SymbolDocId { get; }

    public string Family { get; }

    public int NearestDepth { get; }
}

internal sealed class FileEffectCallSiteRow
{
    public FileEffectCallSiteRow(string enclosingSymbolDocId, string targetSymbolDocId, int line, string family, int nearestDepth)
    {
        EnclosingSymbolDocId = enclosingSymbolDocId;
        TargetSymbolDocId = targetSymbolDocId;
        Line = line;
        Family = family;
        NearestDepth = nearestDepth;
    }

    public string EnclosingSymbolDocId { get; }

    public string TargetSymbolDocId { get; }

    // 1-based source line of the call, as the resident host mined it.
    public int Line { get; }

    public string Family { get; }

    public int NearestDepth { get; }
}

// Methods + call sites are the ANSWER; Status/ReasonCode/ReasonScope/Reason are why the answer is empty.
// They used to be dropped into a Console line, which made "no host", "this file did not compile" and
// "there are genuinely no effects here" one indistinguishable blank screen.
internal sealed class FileEffectReadModel
{
    public FileEffectReadModel(
        IReadOnlyList<FileEffectRow> methods,
        IReadOnlyList<FileEffectCallSiteRow> callSites,
        string status,
        string reasonCode,
        string reasonScope,
        string reason
    )
    {
        Methods = methods;
        CallSites = callSites;
        Status = status;
        ReasonCode = reasonCode;
        ReasonScope = reasonScope;
        Reason = reason;
    }

    public IReadOnlyList<FileEffectRow> Methods { get; }

    public IReadOnlyList<FileEffectCallSiteRow> CallSites { get; }

    // The host's sourceStatus (`exact` / `stale` / `unindexed` / `ambiguous`) or the client-side
    // `unreachable`. Empty only if a host answered without one, which the contract forbids.
    public string Status { get; }

    public string ReasonCode { get; }

    // `file` = persistent and about THIS document, so it is worth a row in Problems. `host` = global and
    // usually transient (booting, reconciling), so it belongs to the status widget and nothing else.
    public string ReasonScope { get; }

    public string Reason { get; }

    public bool IsExact => string.Equals(Status, RigFileEffectHost.SourceExact, System.StringComparison.Ordinal);

    public bool HasFileScopedReason =>
        !IsExact && ReasonCode.Length > 0 && string.Equals(ReasonScope, RigFileEffectHost.ScopeFile, System.StringComparison.Ordinal);
}
