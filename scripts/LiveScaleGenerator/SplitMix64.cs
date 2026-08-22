namespace LiveScaleGenerator;

// Fixed algorithm, independent of runtime implementation details. Do not replace with System.Random.
internal struct SplitMix64(ulong state)
{
    private ulong _state = state;

    public ulong Next()
    {
        var value = (_state += 0x9E3779B97F4A7C15UL);
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    public int Next(int exclusiveUpperBound) => (int)(Next() % (uint)exclusiveUpperBound);
}
