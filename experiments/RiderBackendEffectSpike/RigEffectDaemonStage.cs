using System;
using System.Collections.Generic;
using JetBrains.Application.Resources;
using JetBrains.Application.Settings;
using JetBrains.DocumentModel;
using JetBrains.ReSharper.Daemon.CodeInsights;
using JetBrains.ReSharper.Feature.Services.CSharp.Daemon;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.Rider.Backend.Platform.Icons;
using JetBrains.Rider.Model;
using JetBrains.UI.ThemedIcons;
using JetBrains.Util;

namespace CodeRig.Rider;

[DaemonStage]
internal sealed class RigEffectDaemonStage : CSharpDaemonStageBase
{
    private readonly RigFileEffectHost _host;
    private readonly RigSqlEffectCodeInsightsProvider _sqlCodeInsightsProvider;
    private readonly RigFileEffectCodeInsightsProvider _fileCodeInsightsProvider;
    private readonly IconModel _sqlIcon;
    private readonly IconModel _fileIcon;

    public RigEffectDaemonStage(
        IDaemon daemon,
        RigSqlEffectCodeInsightsProvider sqlCodeInsightsProvider,
        RigFileEffectCodeInsightsProvider fileCodeInsightsProvider,
        IconHost iconHost
    )
    {
        _host = new RigFileEffectHost(daemon);
        _sqlCodeInsightsProvider = sqlCodeInsightsProvider;
        _fileCodeInsightsProvider = fileCodeInsightsProvider;
        _sqlIcon = iconHost.Transform(DatabasesThemedIcons.Query.Id);
        _fileIcon = iconHost.Transform(IdeThemedIcons.FolderOpened.Id);
    }

    protected override IDaemonStageProcess CreateProcess(
        IDaemonProcess process,
        IContextBoundSettingsStore settings,
        DaemonProcessKind processKind,
        ICSharpFile file
    ) =>
        new Process(
            process,
            file,
            _host,
            _sqlCodeInsightsProvider,
            _fileCodeInsightsProvider,
            _sqlIcon,
            _fileIcon,
            processKind == DaemonProcessKind.VISIBLE_DOCUMENT
        );

    private sealed class Process : IDaemonStageProcess
    {
        private readonly ICSharpFile _file;
        private readonly RigFileEffectHost _host;
        private readonly RigSqlEffectCodeInsightsProvider _sqlCodeInsightsProvider;
        private readonly RigFileEffectCodeInsightsProvider _fileCodeInsightsProvider;
        private readonly IconModel _sqlIcon;
        private readonly IconModel _fileIcon;
        private readonly bool _visibleDocument;

        public Process(
            IDaemonProcess daemonProcess,
            ICSharpFile file,
            RigFileEffectHost host,
            RigSqlEffectCodeInsightsProvider sqlCodeInsightsProvider,
            RigFileEffectCodeInsightsProvider fileCodeInsightsProvider,
            IconModel sqlIcon,
            IconModel fileIcon,
            bool visibleDocument
        )
        {
            DaemonProcess = daemonProcess;
            _file = file;
            _host = host;
            _sqlCodeInsightsProvider = sqlCodeInsightsProvider;
            _fileCodeInsightsProvider = fileCodeInsightsProvider;
            _sqlIcon = sqlIcon;
            _fileIcon = fileIcon;
            _visibleDocument = visibleDocument;
        }

        public IDaemonProcess DaemonProcess { get; }

        public void Execute(Action<DaemonStageResult> committer)
        {
            // Rider also runs daemon stages during solution-wide analysis. That pass visited every C# file
            // and turned a focused-file read model into an eager solution scan. The resident query belongs
            // only to the editor-visible pass; all background modes are a strict no-op.
            if (!_visibleDocument)
            {
                committer(new DaemonStageResult(Array.Empty<HighlightingInfo>()));
                return;
            }

            var sourceFile = _file.GetSourceFile();
            if (sourceFile == null)
            {
                committer(new DaemonStageResult(Array.Empty<HighlightingInfo>()));
                return;
            }

            // The daemon also sees source-generator and build-generated C# files.
            // They have no portable file identity in rig's indexed source tree.
            if (sourceFile.Properties.IsGeneratedFile || sourceFile.Properties.IsNonUserFile)
            {
                committer(new DaemonStageResult(Array.Empty<HighlightingInfo>()));
                return;
            }

            var filePath = sourceFile.GetLocation().FullPath;
            var snapshotToken = SnapshotToken(sourceFile);
            if (!_host.TryGet(filePath, snapshotToken, out var model))
            {
                _host.Request(filePath, snapshotToken);
                committer(new DaemonStageResult(Array.Empty<HighlightingInfo>()));
                return;
            }

            // A non-exact answer carries NO rows by contract, so there is nothing to project — but the reason
            // must not vanish. A cause the host scoped to this file gets one row in Problems; a host-scoped
            // cause is left to the status widget (see RigCoverageHighlighting).
            if (!model.IsExact)
            {
                committer(
                    new DaemonStageResult(
                        model.HasFileScopedReason
                            ? new[]
                            {
                                new HighlightingInfo(
                                    FileRange(),
                                    new RigCoverageHighlighting(_file, FileRange(), model.ReasonCode, model.Reason)
                                ),
                            }
                            : Array.Empty<HighlightingInfo>()
                    )
                );
                return;
            }

            var byDocId = new Dictionary<string, List<FileEffectRow>>(StringComparer.Ordinal);
            foreach (var row in model.Methods)
            {
                if (!byDocId.TryGetValue(row.SymbolDocId, out var rows))
                {
                    rows = new List<FileEffectRow>();
                    byDocId.Add(row.SymbolDocId, rows);
                }

                rows.Add(row);
            }

            foreach (var rows in byDocId.Values)
                rows.Sort((left, right) => FamilyRank(left.Family).CompareTo(FamilyRank(right.Family)));

            var highlightings = new List<HighlightingInfo>();
            var projectedMethods = 0;
            foreach (var method in _file.Descendants<IMethodDeclaration>())
            {
                if (DaemonProcess.InterruptFlag)
                    throw new OperationCanceledException();

                if (method.DeclaredElement is not IXmlDocIdOwner docOwner)
                    continue;

                if (!byDocId.TryGetValue(docOwner.XMLDocId, out var rows))
                    continue;

                var range = method.NameIdentifier.GetDocumentRange();
                foreach (var row in rows)
                {
                    highlightings.Add(
                        new HighlightingInfo(
                            range,
                            new CodeInsightsHighlighting(
                                range,
                                displayText: $"rig: {row.Family.ToUpperInvariant()} · depth {row.NearestDepth}",
                                tooltipText: $"rig: reaches {row.Family} · nearest depth {row.NearestDepth}",
                                moreText: string.Empty,
                                ProviderFor(row.Family),
                                method.DeclaredElement,
                                IconFor(row.Family)
                            )
                        )
                    );
                    projectedMethods++;
                }
            }

            // Anchored on the LINE the host mined rather than on a PSI re-resolution of every invocation in
            // the file: resolving the reference is the expensive step, and it is now paid only when one line
            // carries more than one projected target (`Use(Read(), Fetch())`). Two calls to the same target
            // from one body are also distinguishable now, which the (enclosing, target) key could not do.
            var callSitesByLine = new Dictionary<int, List<FileEffectCallSiteRow>>();
            foreach (var row in model.CallSites)
            {
                if (!callSitesByLine.TryGetValue(row.Line, out var rowsOnLine))
                {
                    rowsOnLine = new List<FileEffectCallSiteRow>();
                    callSitesByLine.Add(row.Line, rowsOnLine);
                }

                rowsOnLine.Add(row);
            }

            // A row with an EMPTY target is an effect observed at a call into external code: the host knows the
            // line but has no callee to name, so nothing distinguishes the intended invocation from the others
            // on that line — `await conn.BeginTransactionAsync(t).ConfigureAwait(false)` holds two. Marking both
            // would put a mark on `ConfigureAwait`, so an untargeted row claims only the LEFTMOST invocation on
            // its line. Same limitation as everywhere else here: extraction mines no column.
            var leftmostNameOffsetByLine = new Dictionary<int, int>();
            foreach (var (line, nameRange) in InvocationNameRanges())
            {
                if (!callSitesByLine.ContainsKey(line))
                    continue;

                var offset = nameRange.StartOffset.Offset;
                if (!leftmostNameOffsetByLine.TryGetValue(line, out var known) || offset < known)
                    leftmostNameOffsetByLine[line] = offset;
            }

            var projectedCalls = 0;
            foreach (var invocation in _file.Descendants<IInvocationExpression>())
            {
                if (DaemonProcess.InterruptFlag)
                    throw new OperationCanceledException();

                if (invocation.InvokedExpression is not IReferenceExpression invokedReference)
                    continue;

                var nameRange = invokedReference.NameIdentifier.GetDocumentRange();
                if (!nameRange.IsValid())
                    continue;

                var line = (int)nameRange.Document.GetCoordsByOffset(nameRange.StartOffset.Offset).Line + 1;
                if (!callSitesByLine.TryGetValue(line, out var candidates))
                    continue;

                var rows = MatchOnLine(invocation, invokedReference, candidates);
                if (rows.Count == 0)
                    continue;

                if (
                    rows[0].TargetSymbolDocId.Length == 0
                    && leftmostNameOffsetByLine.TryGetValue(line, out var leftmost)
                    && nameRange.StartOffset.Offset != leftmost
                )
                    continue;

                var range = nameRange;
                foreach (var row in rows)
                    highlightings.Add(new HighlightingInfo(range, RigEffectHighlighting.Create(invocation, range, row)));

                // Second rendering arm: an intra-text adornment anchored on the empty range right after the
                // invoked name, so the hint reads `Foo sql·1(` rather than relying on a text attribute.
                var hintRange = new DocumentRange(range.Document, new TextRange(range.EndOffset.Offset));
                highlightings.Add(new HighlightingInfo(hintRange, new RigEffectInlayHighlighting(invocation, hintRange, rows)));
                projectedCalls++;
            }

            if (highlightings.Count > 0)
                Console.WriteLine(
                    $"[CodeRig Rider] projected methods={projectedMethods}, calls={projectedCalls}, "
                        + $"uiHighlightings={highlightings.Count}, file={filePath}"
                );
            committer(new DaemonStageResult(highlightings));
        }

        // A zero-length range at the top of the file: Problems groups by file, so the anchor only has to be
        // inside it, and a zero-length range cannot underline code that is not at fault.
        private DocumentRange FileRange()
        {
            var range = _file.GetDocumentRange();
            return new DocumentRange(range.Document, new TextRange(range.StartOffset.Offset));
        }

        private IEnumerable<(int Line, DocumentRange NameRange)> InvocationNameRanges()
        {
            foreach (var invocation in _file.Descendants<IInvocationExpression>())
            {
                if (DaemonProcess.InterruptFlag)
                    throw new OperationCanceledException();

                if (invocation.InvokedExpression is not IReferenceExpression invokedReference)
                    continue;

                var nameRange = invokedReference.NameIdentifier.GetDocumentRange();
                if (!nameRange.IsValid())
                    continue;

                yield return ((int)nameRange.Document.GetCoordsByOffset(nameRange.StartOffset.Offset).Line + 1, nameRange);
            }
        }

        // The host's line already picked the invocation; the enclosing DocID stays as a cheap sanity check
        // (no reference resolve). Only a line carrying several projected targets forces the resolve.
        private IReadOnlyList<FileEffectCallSiteRow> MatchOnLine(
            IInvocationExpression invocation,
            IReferenceExpression invokedReference,
            List<FileEffectCallSiteRow> candidates
        )
        {
            if (invocation.GetContainingNode<IMethodDeclaration>()?.DeclaredElement is not IXmlDocIdOwner enclosingOwner)
                return Array.Empty<FileEffectCallSiteRow>();

            var inEnclosing = new List<FileEffectCallSiteRow>();
            foreach (var candidate in candidates)
            {
                if (!string.Equals(candidate.EnclosingSymbolDocId, enclosingOwner.XMLDocId, StringComparison.Ordinal))
                    continue;
                inEnclosing.Add(candidate);
            }

            if (inEnclosing.Count == 0)
                return Array.Empty<FileEffectCallSiteRow>();
            if (inEnclosing.Count == 1 || AllSameTarget(inEnclosing))
                return inEnclosing;

            if (invokedReference.Reference.Resolve().DeclaredElement is not IXmlDocIdOwner targetOwner)
                return Array.Empty<FileEffectCallSiteRow>();

            return inEnclosing.FindAll(candidate =>
                string.Equals(candidate.TargetSymbolDocId, targetOwner.XMLDocId, StringComparison.Ordinal)
            );
        }

        private static bool AllSameTarget(List<FileEffectCallSiteRow> rows)
        {
            var first = rows[0].TargetSymbolDocId;
            for (var i = 1; i < rows.Count; i++)
            {
                if (!string.Equals(rows[i].TargetSymbolDocId, first, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private IconModel IconFor(string family) => string.Equals(family, "file", StringComparison.Ordinal) ? _fileIcon : _sqlIcon;

        private ICodeInsightsProvider ProviderFor(string family) =>
            string.Equals(family, "file", StringComparison.Ordinal) ? _fileCodeInsightsProvider : _sqlCodeInsightsProvider;

        private static int FamilyRank(string family) => string.Equals(family, "sql", StringComparison.Ordinal) ? 0 : 1;

        private static string SnapshotToken(IPsiSourceFile sourceFile)
        {
            // Both stamps are Content Model values already held by Rider; reading them performs no disk IO.
            // The in-memory stamp changes for unsaved edits, while LastWriteTimeUtc changes after a save.
            var inMemory = sourceFile.InMemoryModificationStamp;
            var external = sourceFile.ExternalModificationStamp;
            return $"mem:{inMemory?.ToString() ?? "-"}|ext:{external?.ToString() ?? "-"}|write:{sourceFile.LastWriteTimeUtc.Ticks}";
        }
    }
}
