namespace Rig.Domain.Data;

// Query-scoped forward adjacency. Implementations may derive synthetic nodes lazily, but callers see the
// same immutable CallEdge vocabulary and remain responsible for dispatch/traversal semantics.
public interface IForwardCallSource
{
    IReadOnlyList<CallEdge> CallsFrom(string caller);
}
