using System;
using System.Collections.Generic;
using JetBrains.Application.Settings;
using JetBrains.DocumentModel;
using JetBrains.ReSharper.Daemon.CodeInsights;
using JetBrains.ReSharper.Feature.Services.CSharp.Daemon;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Feature.Services.Resources;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.Rider.Backend.Platform.Icons;
using JetBrains.Rider.Model;
using JetBrains.Util;

namespace CodeRig.Rider;

[DaemonStage]
internal sealed class RigEffectDaemonStage : CSharpDaemonStageBase
{
    private readonly RigFileEffectHost _host;
    private readonly RigEffectCodeInsightsProvider _codeInsightsProvider;
    private readonly IconModel _codeInsightsIcon;

    public RigEffectDaemonStage(IDaemon daemon, RigEffectCodeInsightsProvider codeInsightsProvider, IconHost iconHost)
    {
        _host = new RigFileEffectHost(daemon);
        _codeInsightsProvider = codeInsightsProvider;
        _codeInsightsIcon = iconHost.Transform(DaemonThemedIcons.Recursion.Id);
    }

    protected override IDaemonStageProcess CreateProcess(
        IDaemonProcess process,
        IContextBoundSettingsStore settings,
        DaemonProcessKind processKind,
        ICSharpFile file
    ) => new Process(process, file, _host, _codeInsightsProvider, _codeInsightsIcon, processKind == DaemonProcessKind.VISIBLE_DOCUMENT);

    private sealed class Process : IDaemonStageProcess
    {
        private readonly ICSharpFile _file;
        private readonly RigFileEffectHost _host;
        private readonly RigEffectCodeInsightsProvider _codeInsightsProvider;
        private readonly IconModel _codeInsightsIcon;
        private readonly bool _visibleDocument;

        public Process(
            IDaemonProcess daemonProcess,
            ICSharpFile file,
            RigFileEffectHost host,
            RigEffectCodeInsightsProvider codeInsightsProvider,
            IconModel codeInsightsIcon,
            bool visibleDocument
        )
        {
            DaemonProcess = daemonProcess;
            _file = file;
            _host = host;
            _codeInsightsProvider = codeInsightsProvider;
            _codeInsightsIcon = codeInsightsIcon;
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

            var byDocId = new Dictionary<string, FileEffectRow>(StringComparer.Ordinal);
            foreach (var row in model.Methods)
                byDocId[row.SymbolDocId] = row;

            var highlightings = new List<HighlightingInfo>();
            var projectedMethods = 0;
            foreach (var method in _file.Descendants<IMethodDeclaration>())
            {
                if (DaemonProcess.InterruptFlag)
                    throw new OperationCanceledException();

                if (method.DeclaredElement is not IXmlDocIdOwner docOwner)
                    continue;

                if (!byDocId.TryGetValue(docOwner.XMLDocId, out var row))
                    continue;

                var range = method.NameIdentifier.GetDocumentRange();
                highlightings.Add(
                    new HighlightingInfo(
                        range,
                        new CodeInsightsHighlighting(
                            range,
                            displayText: $"rig: {row.Family.ToUpperInvariant()} · depth {row.NearestDepth}",
                            tooltipText: $"rig: reaches {row.Family} · nearest depth {row.NearestDepth}",
                            moreText: string.Empty,
                            _codeInsightsProvider,
                            method.DeclaredElement,
                            _codeInsightsIcon
                        )
                    )
                );
                projectedMethods++;
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

                var row = MatchOnLine(invocation, invokedReference, candidates);
                if (row == null)
                    continue;

                if (
                    row.TargetSymbolDocId.Length == 0
                    && leftmostNameOffsetByLine.TryGetValue(line, out var leftmost)
                    && nameRange.StartOffset.Offset != leftmost
                )
                    continue;

                var range = nameRange;
                highlightings.Add(new HighlightingInfo(range, new RigEffectHighlighting(invocation, range, row)));

                // Second rendering arm: an intra-text adornment anchored on the empty range right after the
                // invoked name, so the hint reads `Foo sql·1(` rather than relying on a text attribute.
                var hintRange = new DocumentRange(range.Document, new TextRange(range.EndOffset.Offset));
                highlightings.Add(new HighlightingInfo(hintRange, new RigEffectInlayHighlighting(invocation, hintRange, row)));
                projectedCalls++;
            }

            if (highlightings.Count > 0)
                Console.WriteLine(
                    $"[CodeRig Rider] projected methods={projectedMethods}, calls={projectedCalls}, "
                        + $"uiHighlightings={highlightings.Count}, file={filePath}"
                );
            committer(new DaemonStageResult(highlightings));
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
        private static FileEffectCallSiteRow MatchOnLine(
            IInvocationExpression invocation,
            IReferenceExpression invokedReference,
            List<FileEffectCallSiteRow> candidates
        )
        {
            if (invocation.GetContainingNode<IMethodDeclaration>()?.DeclaredElement is not IXmlDocIdOwner enclosingOwner)
                return null;

            FileEffectCallSiteRow inEnclosing = null;
            var matches = 0;
            foreach (var candidate in candidates)
            {
                if (!string.Equals(candidate.EnclosingSymbolDocId, enclosingOwner.XMLDocId, StringComparison.Ordinal))
                    continue;
                matches++;
                inEnclosing = candidate;
            }

            if (matches == 0)
                return null;
            if (matches == 1)
                return inEnclosing;

            if (invokedReference.Reference.Resolve().DeclaredElement is not IXmlDocIdOwner targetOwner)
                return null;

            foreach (var candidate in candidates)
            {
                if (
                    string.Equals(candidate.EnclosingSymbolDocId, enclosingOwner.XMLDocId, StringComparison.Ordinal)
                    && string.Equals(candidate.TargetSymbolDocId, targetOwner.XMLDocId, StringComparison.Ordinal)
                )
                    return candidate;
            }

            return null;
        }

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
