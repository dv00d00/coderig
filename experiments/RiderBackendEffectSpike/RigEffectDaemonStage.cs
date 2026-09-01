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
using JetBrains.ReSharper.Psi.Resolve;
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
            foreach (var (nameIdentifier, declaredElement, memberDocId) in EffectfulMembers())
            {
                if (DaemonProcess.InterruptFlag)
                    throw new OperationCanceledException();

                var rows = RowsFor(BodyDocIds(memberDocId), byDocId);
                if (rows.Count == 0)
                    continue;

                var range = nameIdentifier.GetDocumentRange();
                foreach (var row in rows)
                {
                    highlightings.Add(
                        new HighlightingInfo(
                            range,
                            new CodeInsightsHighlighting(
                                range,
                                displayText: row.NearestDepth == 0
                                    ? $"rig: {row.Family.ToUpperInvariant()} here"
                                    : $"rig: {row.Family.ToUpperInvariant()} · depth {row.NearestDepth}",
                                tooltipText: row.NearestDepth == 0
                                    ? $"rig: performs a {row.Family} effect in this body"
                                    : $"rig: reaches {row.Family} · nearest depth {row.NearestDepth}",
                                moreText: string.Empty,
                                ProviderFor(row.Family),
                                declaredElement,
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
            foreach (var (line, nameRange) in AnchorNameRanges())
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

                var rows = MatchOnLine(invocation, invokedReference.Reference, candidates, anchorIsInvocation: true);
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

            // The THIRD anchor kind: an OBJECT CREATION. `new LinqMetaData(transaction)` IS the db effect in
            // this codebase, and a creation is not an IInvocationExpression — so the depth-0 row on its line
            // had nothing to anchor to and was dropped, which is exactly the row a reader wants marked.
            var projectedCreations = 0;
            foreach (var creation in _file.Descendants<IObjectCreationExpression>())
            {
                if (DaemonProcess.InterruptFlag)
                    throw new OperationCanceledException();

                var creationRange = creation.TypeName?.GetDocumentRange();
                if (creationRange is not { } typeNameRange || !typeNameRange.IsValid())
                    continue;

                var creationLine = (int)typeNameRange.Document.GetCoordsByOffset(typeNameRange.StartOffset.Offset).Line + 1;
                if (!callSitesByLine.TryGetValue(creationLine, out var creationCandidates))
                    continue;

                var creationRows = MatchOnLine(creation, creation.ConstructorReference, creationCandidates, anchorIsInvocation: true);
                if (creationRows.Count == 0)
                    continue;

                if (
                    creationRows[0].TargetSymbolDocId.Length == 0
                    && leftmostNameOffsetByLine.TryGetValue(creationLine, out var creationLeftmost)
                    && typeNameRange.StartOffset.Offset != creationLeftmost
                )
                    continue;

                var creationHintRange = new DocumentRange(typeNameRange.Document, new TextRange(typeNameRange.EndOffset.Offset));
                highlightings.Add(
                    new HighlightingInfo(creationHintRange, new RigEffectInlayHighlighting(creation, creationHintRange, creationRows))
                );
                projectedCreations++;
            }

            // The SECOND anchor kind: a property READ. An effect reached through a getter is keyed to
            // `M:Type.get_X`, and `wizard.IsMeeting` is not an invocation — so before this arm those rows
            // either vanished or (through the removed same-target shortcut) were sprayed onto whatever call
            // happened to share the line. Resolving every reference in the file would be far too expensive,
            // hence the line prefilter: only a line actually carrying an accessor target is resolved.
            var accessorTargetLines = new HashSet<int>();
            foreach (var row in model.CallSites)
            {
                if (row.TargetSymbolDocId.Contains(".get_") || row.TargetSymbolDocId.Contains(".set_"))
                    accessorTargetLines.Add(row.Line);
            }

            var projectedReads = 0;
            foreach (var reference in _file.Descendants<IReferenceExpression>())
            {
                // Descendants is lazy, so a file with no accessor target pays for one step, not a walk.
                if (accessorTargetLines.Count == 0)
                    break;

                if (DaemonProcess.InterruptFlag)
                    throw new OperationCanceledException();

                if (IsInvokedReference(reference))
                    continue;

                var nameRange = reference.NameIdentifier.GetDocumentRange();
                if (!nameRange.IsValid())
                    continue;

                var line = (int)nameRange.Document.GetCoordsByOffset(nameRange.StartOffset.Offset).Line + 1;
                if (!accessorTargetLines.Contains(line) || !callSitesByLine.TryGetValue(line, out var readCandidates))
                    continue;

                var readRows = MatchOnLine(reference, reference.Reference, readCandidates, anchorIsInvocation: false);
                if (readRows.Count == 0)
                    continue;

                var readHintRange = new DocumentRange(nameRange.Document, new TextRange(nameRange.EndOffset.Offset));
                highlightings.Add(new HighlightingInfo(readHintRange, new RigEffectInlayHighlighting(reference, readHintRange, readRows)));
                projectedReads++;
            }

            if (highlightings.Count > 0)
                Console.WriteLine(
                    $"[CodeRig Rider] projected methods={projectedMethods}, calls={projectedCalls}, "
                        + $"creations={projectedCreations}, reads={projectedReads}, "
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

        private IEnumerable<(int Line, DocumentRange NameRange)> AnchorNameRanges()
        {
            foreach (var creation in _file.Descendants<IObjectCreationExpression>())
            {
                var typeNameRange = creation.TypeName?.GetDocumentRange();
                if (typeNameRange is { } range && range.IsValid())
                    yield return ((int)range.Document.GetCoordsByOffset(range.StartOffset.Offset).Line + 1, range);
            }

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

        // The host's line narrows the candidates; the TARGET decides which expression on that line owns them.
        // The resolve used to be skipped whenever every row named the same target ("no ambiguity") — wrong, and
        // visibly so: one target with two invocations on the line handed BOTH the same rows, so
        // `wizard.Validate().IfFailThrowSummary()` rendered the hint twice, and a row targeting a property
        // getter landed on an unrelated call sharing its line. A targeted row is now matched, never assumed.
        private IReadOnlyList<FileEffectCallSiteRow> MatchOnLine(
            ITreeNode anchor,
            IReference reference,
            List<FileEffectCallSiteRow> candidates,
            bool anchorIsInvocation
        )
        {
            var enclosingDocIds = EnclosingDocIds(anchor);
            if (enclosingDocIds.Count == 0)
                return Array.Empty<FileEffectCallSiteRow>();

            var inEnclosing = new List<FileEffectCallSiteRow>();
            foreach (var candidate in candidates)
            {
                foreach (var docId in enclosingDocIds)
                {
                    if (!string.Equals(candidate.EnclosingSymbolDocId, docId, StringComparison.Ordinal))
                        continue;
                    inEnclosing.Add(candidate);
                    break;
                }
            }

            if (inEnclosing.Count == 0)
                return Array.Empty<FileEffectCallSiteRow>();

            var targetDocIds = TargetDocIds(reference);
            var targeted = new List<FileEffectCallSiteRow>();
            var untargeted = new List<FileEffectCallSiteRow>();
            foreach (var row in inEnclosing)
            {
                if (row.TargetSymbolDocId.Length == 0)
                {
                    untargeted.Add(row);
                    continue;
                }

                foreach (var docId in targetDocIds)
                {
                    if (!string.Equals(row.TargetSymbolDocId, docId, StringComparison.Ordinal))
                        continue;
                    targeted.Add(row);
                    break;
                }
            }

            if (targeted.Count > 0)
                return targeted;

            // An untargeted row names no callee (an effect at a call into external code), so it falls back to
            // the leftmost-invocation rule in the caller. A property read never claims one: that is precisely
            // the case the leftmost rule cannot disambiguate.
            return anchorIsInvocation ? untargeted : (IReadOnlyList<FileEffectCallSiteRow>)Array.Empty<FileEffectCallSiteRow>();
        }

        // Anchors for the method-level insight. A ctor and a property are bodies the host keys effects to just
        // like a method; walking only IMethodDeclaration left every expression-bodied property blank while the
        // host was sending rows for it.
        private IEnumerable<(ITreeNode NameIdentifier, IDeclaredElement DeclaredElement, string DocId)> EffectfulMembers()
        {
            foreach (var method in _file.Descendants<IMethodDeclaration>())
            {
                if (method.DeclaredElement is IXmlDocIdOwner owner && method.NameIdentifier != null)
                    yield return (method.NameIdentifier, method.DeclaredElement, owner.XMLDocId);
            }

            foreach (var constructor in _file.Descendants<IConstructorDeclaration>())
            {
                if (constructor.DeclaredElement is IXmlDocIdOwner owner && constructor.NameIdentifier != null)
                    yield return (constructor.NameIdentifier, constructor.DeclaredElement, owner.XMLDocId);
            }

            foreach (var property in _file.Descendants<IPropertyDeclaration>())
            {
                if (property.DeclaredElement is IXmlDocIdOwner owner && property.NameIdentifier != null)
                    yield return (property.NameIdentifier, property.DeclaredElement, owner.XMLDocId);
            }
        }

        // The DocID(s) the host may have keyed a body to. Only bodied ACCESSORS are call-graph nodes in rig, so
        // a property's effects arrive as `M:Type.get_X` / `M:Type.set_X` and never as `P:Type.X`.
        private static IReadOnlyList<string> BodyDocIds(string memberDocId) =>
            memberDocId.StartsWith("P:", StringComparison.Ordinal) ? AccessorDocIds(memberDocId) : new[] { memberDocId };

        // `P:Ns.Type.Name` -> `M:Ns.Type.get_Name` / `set_Name`, carrying an indexer's parameter list along:
        // `P:Ns.Type.Item(System.Int32)` -> `M:Ns.Type.get_Item(System.Int32)`.
        private static IReadOnlyList<string> AccessorDocIds(string propertyDocId)
        {
            var parameters = propertyDocId.IndexOf('(');
            var lastDot = propertyDocId.LastIndexOf('.', parameters < 0 ? propertyDocId.Length - 1 : parameters - 1);
            if (lastDot < 3)
                return new[] { propertyDocId };

            var qualifier = "M:" + propertyDocId.Substring(2, lastDot - 1);
            var name = propertyDocId.Substring(lastDot + 1);
            return new[] { qualifier + "get_" + name, qualifier + "set_" + name };
        }

        private static IReadOnlyList<string> EnclosingDocIds(ITreeNode node)
        {
            if (
                node.GetContainingNode<IAccessorDeclaration>() != null
                && node.GetContainingNode<IPropertyDeclaration>()?.DeclaredElement is IXmlDocIdOwner accessorOwner
            )
                return AccessorDocIds(accessorOwner.XMLDocId);
            if (node.GetContainingNode<IPropertyDeclaration>()?.DeclaredElement is IXmlDocIdOwner propertyOwner)
                return AccessorDocIds(propertyOwner.XMLDocId);
            if (node.GetContainingNode<IMethodDeclaration>()?.DeclaredElement is IXmlDocIdOwner methodOwner)
                return new[] { methodOwner.XMLDocId };
            if (node.GetContainingNode<IConstructorDeclaration>()?.DeclaredElement is IXmlDocIdOwner constructorOwner)
                return new[] { constructorOwner.XMLDocId };
            return Array.Empty<string>();
        }

        private static IReadOnlyList<string> TargetDocIds(IReference reference)
        {
            if (reference?.Resolve().DeclaredElement is not IXmlDocIdOwner targetOwner)
                return Array.Empty<string>();

            var docId = targetOwner.XMLDocId;
            return docId.StartsWith("P:", StringComparison.Ordinal) ? AccessorDocIds(docId) : new[] { docId };
        }

        private static bool IsInvokedReference(IReferenceExpression reference) =>
            reference.Parent is IInvocationExpression invocation && ReferenceEquals(invocation.InvokedExpression, reference);

        // Rows for one member, merged per family at the SHALLOWEST depth: a property contributes through both
        // accessors, and two rows saying `db` differ only in how far away the effect is.
        private static List<FileEffectRow> RowsFor(IReadOnlyList<string> docIds, Dictionary<string, List<FileEffectRow>> byDocId)
        {
            var byFamily = new Dictionary<string, FileEffectRow>(StringComparer.Ordinal);
            foreach (var docId in docIds)
            {
                if (!byDocId.TryGetValue(docId, out var rows))
                    continue;

                foreach (var row in rows)
                {
                    if (!byFamily.TryGetValue(row.Family, out var known) || row.NearestDepth < known.NearestDepth)
                        byFamily[row.Family] = row;
                }
            }

            var merged = new List<FileEffectRow>(byFamily.Values);
            merged.Sort((left, right) => FamilyRank(left.Family).CompareTo(FamilyRank(right.Family)));
            return merged;
        }

        private IconModel IconFor(string family) => string.Equals(family, "file", StringComparison.Ordinal) ? _fileIcon : _sqlIcon;

        private ICodeInsightsProvider ProviderFor(string family) =>
            string.Equals(family, "file", StringComparison.Ordinal) ? _fileCodeInsightsProvider : _sqlCodeInsightsProvider;

        private static int FamilyRank(string family) => RigEffectFamilyStyle.Rank(family);

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
