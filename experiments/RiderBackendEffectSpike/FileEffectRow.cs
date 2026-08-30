namespace RiderBackendEffectSpike;

internal sealed class FileEffectRow
{
    public FileEffectRow(string symbolDocId, string effectKind, int reachableEffectCount)
    {
        SymbolDocId = symbolDocId;
        EffectKind = effectKind;
        ReachableEffectCount = reachableEffectCount;
    }

    public string SymbolDocId { get; }

    public string EffectKind { get; }

    public int ReachableEffectCount { get; }
}
