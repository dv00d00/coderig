using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Rig.Analysis.Rules;
using Rig.Cli.CommandLine;
using Rig.Cli.Rendering;
using Rig.Cli.Services;
using Rig.Storage.Queries;
using static Rig.Cli.Graph.TraversalGraphLoader;
using static Rig.Cli.Rendering.SymbolNameFormatter;

namespace Rig.Cli.Web;

internal static class FileEffectsEndpoint
{
    private const int DefaultFileLimit = 100;
    private const int MaxFileLimit = 500;
    private const int DefaultSourceLines = 240;
    private const int MaxSourceLines = SourceRenderer.DefaultMaxLines;
    private const int ShortShaLength = 12;

    internal static void MapFileEffects(this WebApplication app, string workingDirectory)
    {
        app.MapGet(
            "/api/files",
            async (string? q, int? limit, string? store) =>
            {
                try
                {
                    var selectedLimit = Math.Clamp(limit ?? DefaultFileLimit, 1, MaxFileLimit);
                    await using var context = await OpenReadContextGatedAsync(new WorkspaceLocation(workingDirectory, NullIfBlank(store)));
                    var query = context.SourceFiles.AsNoTracking().Where(file => file.Status != "skipped");
                    if (!string.IsNullOrWhiteSpace(q))
                    {
                        var pattern = q.Trim();
                        query = query.Where(file => file.FilePath.Contains(pattern));
                    }

                    var rows = await query
                        .Select(file => new
                        {
                            file.FilePath,
                            file.ProjectName,
                            file.Status,
                        })
                        .ToListAsync();
                    var files = rows.GroupBy(row => row.FilePath, FilePathComparer)
                        .OrderBy(group => group.Key, FilePathComparer)
                        .Select(group => new IndexedFileDto(
                            group.Key,
                            Path.GetFileName(group.Key),
                            group.Select(row => row.Status).FirstOrDefault() ?? "indexed",
                            group
                                .Select(row => row.ProjectName)
                                .Where(project => !string.IsNullOrWhiteSpace(project))
                                .Distinct(StringComparer.Ordinal)
                                .OrderBy(project => project, StringComparer.Ordinal)
                                .ToArray()
                        ))
                        .ToArray();
                    return Results.Json(new IndexedFilesResponseDto(files.Take(selectedLimit).ToArray(), files.Length, selectedLimit));
                }
                catch (Exception ex)
                {
                    return Results.Problem(title: "File inventory failed", detail: ex.Message, statusCode: 400);
                }
            }
        );

        app.MapGet(
            // The filter vocabulary the sibling endpoints already speak, applied to the PROJECTION. None of it
            // touches the cached artifact: the resident solution-wide closure is keyed on (store, rules,
            // schema) and serves every filtered view, so a client may drive arbitrary combinations at no cost,
            // and the badge numbers cannot move between two views of the same file. `intrinsic` and `async`
            // are absent on purpose - both would change the closure, so both need their own cache key first.
            "/api/file-effects",
            async (
                string? file,
                string? store,
                string[]? only,
                string[]? exclude,
                int? minDepth,
                int? maxDepth,
                bool? direct,
                bool? looped,
                bool? noDispatch
            ) =>
            {
                if (string.IsNullOrWhiteSpace(file))
                {
                    return Results.Problem(title: "Missing 'file'", detail: "Choose a path returned by /api/files.", statusCode: 400);
                }

                try
                {
                    var artifact = await FileEffectsQueryService.BuildResidentAsync(workingDirectory, file, NullIfBlank(store));
                    var notes = new List<string>();
                    var onlyFamilies = FileEffectFilterTokens.Resolution.Empty;
                    var excludeFamilies = FileEffectFilterTokens.Resolution.Empty;
                    if (only is { Length: > 0 } || exclude is { Length: > 0 })
                    {
                        var rules = RuleSetLoader.Load(workingDirectory, extraRules: [], loadedPaths: out _);
                        onlyFamilies = FileEffectFilterTokens.Resolve(rules, only ?? [], "only");
                        excludeFamilies = FileEffectFilterTokens.Resolve(rules, exclude ?? [], "exclude");
                        notes.AddRange(onlyFamilies.Notes);
                        notes.AddRange(excludeFamilies.Notes);
                    }

                    var filter = new FileEffectLens.LensFilter(
                        Only: only is { Length: > 0 } ? onlyFamilies.Families : null,
                        Exclude: exclude is { Length: > 0 } ? excludeFamilies.Families : null,
                        MinDepth: minDepth,
                        MaxDepth: maxDepth,
                        DirectOnly: direct ?? false,
                        LoopedOnly: looped ?? false,
                        HideDispatchOnly: noDispatch ?? false
                    );
                    return Results.Json(ToResponse(artifact, filter, notes));
                }
                catch (Exception ex)
                {
                    return Results.Problem(title: "File effects query failed", detail: ex.Message, statusCode: 400);
                }
            }
        );

        // TIERS 1-3 for one file. Separate from /api/file-effects on purpose: a different derivation with a
        // different cost, fetched in parallel by the lens so badges render the instant the effect query answers
        // and the finding marks fold in when these arrive. It replaces a client-side fixture — see the
        // `filelens-findings` history in the backlog card.
        app.MapGet(
            "/api/file-findings",
            async (string? file, string? store) =>
            {
                if (string.IsNullOrWhiteSpace(file))
                {
                    return Results.Problem(title: "Missing 'file'", detail: "Choose a path returned by /api/files.", statusCode: 400);
                }

                try
                {
                    var findings = await FileFindingsQueryService.ForFileAsync(workingDirectory, file, NullIfBlank(store));
                    return Results.Json(ToFindingsResponse(file, findings));
                }
                catch (Exception ex)
                {
                    return Results.Problem(title: "File findings query failed", detail: ex.Message, statusCode: 400);
                }
            }
        );

        app.MapGet(
            "/api/file-source",
            async (string? file, int? start, int? count, string? store) =>
            {
                if (string.IsNullOrWhiteSpace(file))
                {
                    return Results.Problem(title: "Missing 'file'", detail: "Choose a path returned by /api/files.", statusCode: 400);
                }

                try
                {
                    var first = Math.Max(1, start ?? 1);
                    var take = Math.Clamp(count ?? DefaultSourceLines, 1, MaxSourceLines);
                    await using var context = await OpenReadContextGatedAsync(new WorkspaceLocation(workingDirectory, NullIfBlank(store)));
                    var indexed = await context.SourceFiles.AsNoTracking().AnyAsync(row => row.FilePath == file && row.Status != "skipped");
                    if (!indexed)
                    {
                        return Results.Problem(
                            title: "Unknown indexed file",
                            detail: "The requested path was not returned by this store's indexed file inventory.",
                            statusCode: 400
                        );
                    }

                    var runs = await Reads.ListRunsAsync(context);
                    var run = runs.FirstOrDefault(candidate => candidate.SourceCommit is not null) ?? runs.FirstOrDefault();
                    var storeDirty = run?.SourceDirty ?? false;
                    var renderer = new SourceRenderer(run?.SourceCommit, storeDirty);
                    // Ask for one look-ahead line. It is not returned; it only tells the client whether a next
                    // page exists without reading or counting the whole source file.
                    var snippet = renderer.Resolve(file, first, first + take, maxLines: take + 1);
                    var shown = snippet.Lines.Take(take).ToArray();
                    return Results.Json(
                        new FileSourceResponseDto(
                            file,
                            first,
                            shown.Length == 0 ? first : shown[^1].Number,
                            OriginName(snippet.Origin),
                            Short(snippet.Commit),
                            snippet.Reason,
                            shown.Select(line => new SourceLineDto(line.Number, line.Text)).ToArray(),
                            HasPrevious: first > 1,
                            HasMore: snippet.Lines.Count > take || snippet.TruncatedCount > 0,
                            StoreDirty: storeDirty
                        )
                    );
                }
                catch (Exception ex)
                {
                    return Results.Problem(title: "File source query failed", detail: ex.Message, statusCode: 400);
                }
            }
        );
    }

    internal static FileEffectsResponseDto ToResponse(FileEffectsQueryService.Artifact artifact) =>
        ToResponse(artifact, FileEffectLens.LensFilter.None, []);

    internal static FileEffectsResponseDto ToResponse(
        FileEffectsQueryService.Artifact artifact,
        FileEffectLens.LensFilter filter,
        IReadOnlyList<string> filterNotes
    )
    {
        var model = artifact.Model;
        // Method naming, ordering and badge order come from the shared lens (FileEffectLens), which
        // `rig annotate` renders too — the two surfaces cannot disagree about what a method reaches.
        var lens = FileEffectLens.Project(artifact, filter);
        var methods = lens
            .Methods.Select(method => new FileEffectMethodDto(
                method.SymbolId,
                method.Name,
                method.Signature,
                method.Line,
                method.EndLine,
                method
                    .Badges.Select(badge => new FileEffectAggregateDto(
                        badge.Family,
                        badge.NearestDepth,
                        badge.ViaDispatchOnly,
                        badge.Looped
                    ))
                    .ToArray()
            ))
            .ToArray();
        // Site rows are the RAW read model (the client does its own per-line merge), so the filter cannot be
        // applied to them badge-by-badge: `minDepth` on unmerged rows would keep a distant row the merged line
        // badge had already lost, and the browser would render a distance the terminal does not. The lens is
        // the authority instead — a site keeps a family only if that family survived on that LINE.
        var survivingByLine = lens.Lines.ToDictionary(
            line => line.Line,
            line => line.Badges.Select(badge => badge.Family).ToHashSet(StringComparer.Ordinal)
        );
        var sites = model
            .CallSites.Select(site => new FileEffectCallSiteDto(
                site.EnclosingSymbolId,
                site.TargetSymbolId,
                site.Line,
                (
                    filter.IsActive
                        ? site.Effects.Where(effect =>
                            survivingByLine.TryGetValue(site.Line, out var families) && families.Contains(effect.Family)
                        )
                        : site.Effects
                )
                    .Select(Map)
                    .ToArray()
            ))
            .Where(site => site.Effects.Count > 0)
            .OrderBy(site => site.Line)
            .ThenBy(site => site.TargetMethodId, StringComparer.Ordinal)
            .ToArray();
        var declarations = artifact
            .Methods.Values.OrderBy(method => method.Line)
            .ThenBy(method => method.Id, StringComparer.Ordinal)
            .Select(method => new FileEffectDeclarationDto(method.Id, method.Name, method.Signature, method.Line, method.EndLine))
            .ToArray();
        var disclosure = lens.Disclosure;
        return new FileEffectsResponseDto(
            model.FilePath,
            model.EffectSelectors,
            methods,
            sites,
            ColumnsAvailable: false,
            WitnessPathsIncluded: false,
            declarations,
            disclosure.Active || filterNotes.Count > 0
                ? new FileEffectsFilterDto(
                    disclosure.Active,
                    disclosure.HiddenBadges,
                    disclosure.HiddenMethods,
                    disclosure.HiddenLines,
                    filterNotes
                )
                : null
        );
    }

    // The findings wire mapping, extracted so it can be pinned by a test rather than only by reading it. The
    // renames are the whole reason: `Reason` is the hazard SUBTYPE and `Context` is the key / iteration kind,
    // names that mean nothing to a client, and a silent swap of those two columns would be invisible in the UI
    // (both are short lowercase strings) while making every tooltip wrong.
    internal static FileFindingsResponseDto ToFindingsResponse(string file, FileFindingsQueryService.Findings findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        return new FileFindingsResponseDto(
            file,
            findings
                .Hazards.Select(hazard => new FileHazardDto(
                    hazard.Type,
                    hazard.Confidence,
                    hazard.Reason,
                    hazard.Context,
                    ShortName(hazard.Enclosing),
                    hazard.Line,
                    hazard.Detail
                ))
                .ToArray(),
            findings
                .Amplifications.Select(amplification => new FileAmplificationDto(
                    amplification.Type,
                    amplification.Confidence,
                    amplification.Reason,
                    amplification.Context,
                    ShortName(amplification.Enclosing),
                    amplification.Line,
                    amplification.Detail,
                    amplification.Provider,
                    amplification.Operation
                ))
                .ToArray(),
            findings
                .Anchors.Select(anchor => new FileAnchorDto(
                    anchor.Line,
                    ShortName(anchor.Caller),
                    anchor.IterationKind,
                    anchor.WitnessProvider,
                    anchor.WitnessOperation,
                    anchor.WitnessResource,
                    anchor.WitnessDepth,
                    anchor.Confidence,
                    anchor.Evidence,
                    anchor.Guards,
                    anchor.DispatchBasis,
                    anchor.DispatchDegree
                ))
                .ToArray(),
            // A tier-3 count of zero is ambiguous on its own — no anchors in this file, or no rule section at
            // all. The service returns an empty list in both cases, so the flag is the only place the
            // difference can be recorded; it is set from whether the derivation ran, not from the count.
            CrossMethodAvailable: findings.CrossMethodDerived
        );
    }

    private static FileEffectAggregateDto Map(Rig.Domain.Functions.FileEffectAggregate effect) =>
        new(effect.Family, effect.NearestDepth, effect.ViaDispatchOnly, effect.Looped);

    private static string OriginName(SourceOrigin origin) =>
        origin switch
        {
            SourceOrigin.WorkingTree => "worktree",
            SourceOrigin.GitBlob => "git",
            _ => "unavailable",
        };

    private static string? Short(string? sha) =>
        string.IsNullOrEmpty(sha) ? null
        : sha.Length <= ShortShaLength ? sha
        : sha[..ShortShaLength];

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static readonly StringComparer FilePathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
