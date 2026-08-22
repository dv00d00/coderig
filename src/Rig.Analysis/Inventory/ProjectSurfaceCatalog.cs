using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Rig.Domain.Data;
using RuleSet = Rig.Domain.Data.RuleSet;

namespace Rig.Analysis.Inventory;

// A Roslyn-free contribution produced while one emitter's semantic model is still alive. Project identity
// is carried as stable strings so FileFacts never pins a Project/Solution/Compilation.
internal sealed record ProjectSurfaceContribution(
    string ProjectName,
    string ProjectFilePath,
    string AssemblyName,
    ProjectSurfaceShard Shard,
    bool IsClassifiable
);

internal sealed record ProjectSurfaceRefresh(
    ImmutableArray<ProjectSurfaceShard> GeneratedShards,
    ProjectSurfaceShard MetaShard,
    bool IsClassifiable,
    ImmutableDictionary<string, FileFacts>? GeneratedFacts = null
);

internal delegate Task<ProjectSurfaceRefresh> ResidentSurfaceRefresher(
    Solution solution,
    ProjectId projectId,
    RuleSet rules,
    CancellationToken cancellationToken,
    Rig.Analysis.Extraction.StringInterner? interner
);

// Immutable per-project surface partitions captured by FactSnapshot. Ordinary source shards are replaced
// with the same emitter grain as FileFacts; generated/meta shards move only when refinement explicitly asks
// Roslyn to refresh them. LastAcceptedSurfaceHash is the comparison point for the next edit, not the cold
// boot forever.
internal sealed record ProjectSurfacePartition(
    string ProjectName,
    string ProjectFilePath,
    string AssemblyName,
    ImmutableDictionary<string, ProjectSurfaceShard> SourceShards,
    ImmutableArray<ProjectSurfaceShard> GeneratedShards,
    ProjectSurfaceShard MetaShard,
    string LastAcceptedSurfaceHash,
    bool IsClassifiable,
    bool RequiresCoarseReconciliation,
    bool GateDisabled
)
{
    internal string Aggregate() => ProjectSurfaceBuilder.Aggregate(SourceShards.Values.Concat(GeneratedShards).Append(MetaShard));
}

internal sealed class ProjectSurfaceCatalog
{
    private readonly ImmutableDictionary<ProjectId, ProjectSurfacePartition> _projects;
    private readonly ImmutableHashSet<string> _unresolvedEmitterPaths;

    private ProjectSurfaceCatalog(
        ImmutableDictionary<ProjectId, ProjectSurfacePartition> projects,
        ImmutableHashSet<string>? unresolvedEmitterPaths = null
    )
    {
        _projects = projects;
        _unresolvedEmitterPaths = unresolvedEmitterPaths ?? ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);
    }

    internal static ProjectSurfaceCatalog Empty { get; } = new(ImmutableDictionary<ProjectId, ProjectSurfacePartition>.Empty);

    internal IReadOnlyDictionary<ProjectId, ProjectSurfacePartition> Projects => _projects;

    internal int GateDisabledCount => _projects.Count(pair => pair.Value.GateDisabled);

    internal IReadOnlyCollection<string> GateDisabledProjectNames =>
        _projects
            .Values.Where(partition => partition.GateDisabled)
            .Select(partition => partition.ProjectName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    internal static ProjectSurfaceCatalog Seed(Solution solution, IReadOnlyList<ProjectSurfaceSnapshot>? surfaces)
    {
        if (surfaces is null || surfaces.Count == 0)
        {
            return Empty;
        }

        var projectsByPath = solution
            .Projects.Where(p => !string.IsNullOrWhiteSpace(p.FilePath))
            .GroupBy(p => Path.GetFullPath(p.FilePath!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
        var projectsByName = solution
            .Projects.GroupBy(p => p.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var surfacesByName = surfaces
            .GroupBy(s => s.ProjectName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var emitterPathCounts = surfaces
            .SelectMany(s => s.Shards)
            .Where(s => s.EmitterFilePath.Length > 0)
            .GroupBy(s => Path.GetFullPath(s.EmitterFilePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var builder = ImmutableDictionary.CreateBuilder<ProjectId, ProjectSurfacePartition>();
        var unresolvedEmitterPaths = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var surface in surfaces)
        {
            Project? project = null;
            if (!string.IsNullOrWhiteSpace(surface.ProjectFilePath))
            {
                var path = Path.GetFullPath(surface.ProjectFilePath);
                if (projectsByPath.TryGetValue(path, out var matches) && matches.Length == 1)
                {
                    project = matches[0];
                }
            }
            else if (
                projectsByName.TryGetValue(surface.ProjectName, out var nameMatches)
                && nameMatches.Length == 1
                && surfacesByName[surface.ProjectName].Length == 1
            )
            {
                // Name fallback is deliberately collision-intolerant. An ambiguous match is unavailable,
                // which keeps the coarse debt rather than classifying the wrong project BodyOnly.
                project = nameMatches[0];
            }

            if (project is null || builder.ContainsKey(project.Id))
            {
                unresolvedEmitterPaths.UnionWith(
                    surface.Shards.Where(s => s.EmitterFilePath.Length > 0).Select(s => Path.GetFullPath(s.EmitterFilePath))
                );
                continue;
            }

            var meta = surface.Shards.Where(s => !s.IsGenerated && s.EmitterFilePath.Length == 0).ToArray();
            var sources = surface.Shards.Where(s => !s.IsGenerated && s.EmitterFilePath.Length > 0).ToArray();
            var duplicateSource = sources
                .GroupBy(s => Path.GetFullPath(s.EmitterFilePath), StringComparer.OrdinalIgnoreCase)
                .Any(g => g.Count() != 1);
            var sourceMap = sources
                .GroupBy(s => Path.GetFullPath(s.EmitterFilePath), StringComparer.OrdinalIgnoreCase)
                .ToImmutableDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var classifiable =
                surface.SurfaceHash.Length > 0
                && meta.Length == 1
                && !duplicateSource
                && sources.All(s => s.SurfaceHash.Length > 0)
                // Generated facts still use the path-global FileFacts replacement grain. If any
                // generated emitter path is shared with another source/generated contribution, a
                // one-project refresh could erase the other contribution. Disable the optimization
                // for both partitions and retain coarse debt until replacement identity is widened.
                && surface
                    .Shards.Where(s => s.IsGenerated)
                    .All(s =>
                        s.EmitterFilePath.Length > 0 && emitterPathCounts.GetValueOrDefault(Path.GetFullPath(s.EmitterFilePath)) == 1
                    );
            builder[project.Id] = new ProjectSurfacePartition(
                surface.ProjectName,
                surface.ProjectFilePath,
                surface.AssemblyName,
                sourceMap,
                surface.Shards.Where(s => s.IsGenerated).ToImmutableArray(),
                meta.Length == 1 ? meta[0] : new ProjectSurfaceShard("", false, ""),
                surface.SurfaceHash,
                classifiable,
                RequiresCoarseReconciliation: false,
                GateDisabled: false
            );
        }

        return new ProjectSurfaceCatalog(builder.ToImmutable(), unresolvedEmitterPaths.ToImmutable());
    }

    internal ProjectSurfaceCatalog ReplaceEmitters(Solution solution, IEnumerable<ProjectSurfaceContribution> contributions)
    {
        var builder = _projects.ToBuilder();
        foreach (var contribution in contributions)
        {
            var projectId = ResolveProject(solution, contribution);
            if (projectId is null || !builder.TryGetValue(projectId, out var partition))
            {
                continue;
            }

            var path = Path.GetFullPath(contribution.Shard.EmitterFilePath);
            var sources = partition.SourceShards.SetItem(path, contribution.Shard with { EmitterFilePath = path });
            builder[projectId] = partition with
            {
                SourceShards = sources,
                IsClassifiable = partition.IsClassifiable && contribution.IsClassifiable,
            };
        }

        return new ProjectSurfaceCatalog(builder.ToImmutable(), _unresolvedEmitterPaths);
    }

    internal bool TryApplyRefresh(
        ProjectId projectId,
        ProjectSurfaceRefresh refresh,
        out ProjectSurfaceCatalog catalog,
        out SurfaceState state
    )
    {
        catalog = this;
        state = SurfaceState.Unknown;
        if (
            !_projects.TryGetValue(projectId, out var partition)
            || !partition.IsClassifiable
            || !refresh.IsClassifiable
            || refresh.MetaShard.SurfaceHash.Length == 0
            || refresh.GeneratedShards.Any(s => s.SurfaceHash.Length == 0)
            || !GeneratedPathsAreUnambiguous(projectId, refresh.GeneratedShards)
        )
        {
            return false;
        }

        var refreshed = partition with { GeneratedShards = refresh.GeneratedShards, MetaShard = refresh.MetaShard };
        var aggregate = refreshed.Aggregate();
        state =
            !partition.GateDisabled && string.Equals(aggregate, partition.LastAcceptedSurfaceHash, StringComparison.Ordinal)
                ? SurfaceState.BodyOnly
                : SurfaceState.Changed;
        refreshed = refreshed with { LastAcceptedSurfaceHash = aggregate };
        if (state == SurfaceState.Changed)
        {
            refreshed = refreshed with { RequiresCoarseReconciliation = true };
        }
        catalog = new ProjectSurfaceCatalog(_projects.SetItem(projectId, refreshed), _unresolvedEmitterPaths);
        return true;
    }

    internal ProjectSurfaceCatalog MarkReconciled(IEnumerable<ProjectId> projectIds)
    {
        var builder = _projects.ToBuilder();
        foreach (var projectId in projectIds)
        {
            if (builder.TryGetValue(projectId, out var partition) && partition.RequiresCoarseReconciliation)
            {
                builder[projectId] = partition with { RequiresCoarseReconciliation = false };
            }
        }
        return new ProjectSurfaceCatalog(builder.ToImmutable(), _unresolvedEmitterPaths);
    }

    internal ProjectSurfaceCatalog MarkGateDisabled(IEnumerable<ProjectId> projectIds)
    {
        var builder = _projects.ToBuilder();
        foreach (var projectId in projectIds)
        {
            if (builder.TryGetValue(projectId, out var partition))
            {
                builder[projectId] = partition with { GateDisabled = true, RequiresCoarseReconciliation = false };
            }
        }
        return new ProjectSurfaceCatalog(builder.ToImmutable(), _unresolvedEmitterPaths);
    }

    private bool GeneratedPathsAreUnambiguous(ProjectId projectId, ImmutableArray<ProjectSurfaceShard> generatedShards)
    {
        var paths = generatedShards.Select(s => s.EmitterFilePath.Length == 0 ? "" : Path.GetFullPath(s.EmitterFilePath)).ToArray();
        if (paths.Any(p => p.Length == 0) || paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
        {
            return false;
        }

        var occupied = _projects
            .SelectMany(pair =>
                pair.Value.SourceShards.Keys.Concat(pair.Key == projectId ? [] : pair.Value.GeneratedShards.Select(s => s.EmitterFilePath))
            )
            .Where(p => p.Length > 0)
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        occupied.UnionWith(_unresolvedEmitterPaths);
        return paths.All(p => !occupied.Contains(p));
    }

    private static ProjectId? ResolveProject(Solution solution, ProjectSurfaceContribution contribution)
    {
        if (!string.IsNullOrWhiteSpace(contribution.ProjectFilePath))
        {
            var fullPath = Path.GetFullPath(contribution.ProjectFilePath);
            var byPath = solution
                .Projects.Where(p =>
                    p.FilePath is not null && string.Equals(Path.GetFullPath(p.FilePath), fullPath, StringComparison.OrdinalIgnoreCase)
                )
                .Select(p => p.Id)
                .ToArray();
            return byPath.Length == 1 ? byPath[0] : null;
        }

        var byName = solution.Projects.Where(p => p.Name == contribution.ProjectName).Select(p => p.Id).ToArray();
        return byName.Length == 1 ? byName[0] : null;
    }
}
