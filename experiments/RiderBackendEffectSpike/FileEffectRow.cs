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
    public FileEffectCallSiteRow(string enclosingSymbolDocId, string targetSymbolDocId, string family, int nearestDepth)
    {
        EnclosingSymbolDocId = enclosingSymbolDocId;
        TargetSymbolDocId = targetSymbolDocId;
        Family = family;
        NearestDepth = nearestDepth;
    }

    public string EnclosingSymbolDocId { get; }

    public string TargetSymbolDocId { get; }

    public string Family { get; }

    public int NearestDepth { get; }
}

internal sealed class FileEffectReadModel
{
    public FileEffectReadModel(IReadOnlyList<FileEffectRow> methods, IReadOnlyList<FileEffectCallSiteRow> callSites)
    {
        Methods = methods;
        CallSites = callSites;
    }

    public IReadOnlyList<FileEffectRow> Methods { get; }

    public IReadOnlyList<FileEffectCallSiteRow> CallSites { get; }
}
