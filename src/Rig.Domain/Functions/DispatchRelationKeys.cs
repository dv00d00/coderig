using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// Shared normalization for the keyed relation neighborhoods FactPathFinder consumes during dispatch.
// Keeping the resident indexes and the traversal engine on these keys prevents demand loading from
// silently omitting a relation that the whole-graph engine would use.
public static class DispatchRelationKeys
{
    public static string RelatedFamily(string relatedTypeId) => TypeClosure.StripGeneric(relatedTypeId);

    public static string? UnresolvedInterfaceName(TypeRelationFact relation) =>
        relation.RelationKind == RelationKinds.Interface && relation.RelatedSymbolId.StartsWith("!:", StringComparison.Ordinal)
            ? SimpleTypeName(relation.RelatedSymbolId)
            : null;

    // Simple (un-namespaced, arity-stripped) name from a type DocID:
    // "T:Ns.IFoo`1" / "!:IFoo" -> "IFoo".
    public static string SimpleTypeName(string typeId)
    {
        var value = typeId;
        if (value.Length >= 2 && value[1] == ':')
        {
            value = value.Substring(2);
        }

        var lastDot = value.LastIndexOf('.');
        if (lastDot >= 0)
        {
            value = value.Substring(lastDot + 1);
        }

        var tick = value.IndexOf('`');
        return tick < 0 ? value : value.Substring(0, tick);
    }
}
