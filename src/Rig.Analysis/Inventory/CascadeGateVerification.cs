using System.Collections.Immutable;
using Rig.Domain.Data;

namespace Rig.Analysis.Inventory;

// Pure, Roslyn-free verification at FileFacts' replacement grain. ProjectSurfaces are deliberately
// excluded: they are the gate input being verified, while every query-visible fact kind is evidence.
internal static class CascadeGateVerification
{
    internal static bool Matches(FileFacts current, FileFacts fresh) =>
        MultisetEquals(current.SourceFiles, fresh.SourceFiles)
        && MultisetEquals(current.DiRegistrations, fresh.DiRegistrations)
        && MultisetEquals(current.Symbols, fresh.Symbols)
        && MultisetEquals(current.References, fresh.References)
        && MultisetEquals(current.TypeRelations, fresh.TypeRelations)
        && MultisetEquals(current.Dispatch, fresh.Dispatch)
        && MultisetEquals(current.Allocations, fresh.Allocations)
        && MultisetEquals(current.CompileHealth, fresh.CompileHealth);

    internal static FileFacts CurrentPathSlice(AnalysisResult baseFacts, ImmutableDictionary<string, FileFacts> overlay, string filePath)
    {
        if (overlay.TryGetValue(filePath, out var replacement))
        {
            return replacement;
        }

        return new FileFacts(
            baseFacts.SourceFiles.Where(row => SamePath(row.FilePath, filePath)).ToImmutableArray(),
            baseFacts
                .DiRegistrations.Where(row =>
                    row.FilePath.Length > 0
                    && string.Equals(Path.GetExtension(row.FilePath), ".cs", StringComparison.OrdinalIgnoreCase)
                    && SamePath(row.FilePath, filePath)
                )
                .ToImmutableArray(),
            (baseFacts.Symbols ?? []).Where(row => SamePath(row.FilePath, filePath)).ToImmutableArray(),
            (baseFacts.References ?? []).Where(row => SamePath(row.FilePath, filePath)).ToImmutableArray(),
            (baseFacts.TypeRelations ?? []).Where(row => SamePath(row.FilePath, filePath)).ToImmutableArray(),
            (baseFacts.DispatchFacts ?? []).Where(row => SamePath(row.FilePath, filePath)).ToImmutableArray(),
            (baseFacts.AllocationFacts ?? []).Where(row => SamePath(row.FilePath, filePath)).ToImmutableArray(),
            (baseFacts.CompilationHealth?.Files ?? []).Where(row => SamePath(row.FilePath, filePath)).ToImmutableArray()
        );
    }

    private static bool MultisetEquals<T>(ImmutableArray<T> left, ImmutableArray<T> right)
        where T : notnull
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        var counts = new Dictionary<T, int>();
        foreach (var item in left)
        {
            counts[item] = counts.GetValueOrDefault(item) + 1;
        }
        foreach (var item in right)
        {
            if (!counts.TryGetValue(item, out var count))
            {
                return false;
            }

            if (count == 1)
            {
                counts.Remove(item);
            }
            else
            {
                counts[item] = count - 1;
            }
        }

        return counts.Count == 0;
    }

    private static bool SamePath(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
