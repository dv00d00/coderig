namespace RiderBackendEffectSpike;

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
