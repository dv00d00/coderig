using System;
using System.Collections.Generic;
using JetBrains.Application.Settings;
using JetBrains.ReSharper.Feature.Services.CSharp.Daemon;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Tree;

namespace RiderBackendEffectSpike;

[DaemonStage]
internal sealed class RigEffectDaemonStage : CSharpDaemonStageBase
{
    private readonly RigFileEffectHost _host;

    public RigEffectDaemonStage(IDaemon daemon)
    {
        Console.WriteLine("[rig-spike] daemon stage constructed");
        _host = new RigFileEffectHost(daemon);
    }

    protected override IDaemonStageProcess CreateProcess(
        IDaemonProcess process,
        IContextBoundSettingsStore settings,
        DaemonProcessKind processKind,
        ICSharpFile file
    ) => new Process(process, file, _host, processKind == DaemonProcessKind.VISIBLE_DOCUMENT);

    private sealed class Process : IDaemonStageProcess
    {
        private readonly ICSharpFile _file;
        private readonly RigFileEffectHost _host;
        private readonly bool _visibleDocument;

        public Process(IDaemonProcess daemonProcess, ICSharpFile file, RigFileEffectHost host, bool visibleDocument)
        {
            DaemonProcess = daemonProcess;
            _file = file;
            _host = host;
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
            if (!_host.TryGet(filePath, snapshotToken, out var rows))
            {
                _host.Request(filePath, snapshotToken);
                committer(new DaemonStageResult(Array.Empty<HighlightingInfo>()));
                return;
            }

            var byDocId = new Dictionary<string, FileEffectRow>(StringComparer.Ordinal);
            foreach (var row in rows)
                byDocId[row.SymbolDocId] = row;

            var highlightings = new List<HighlightingInfo>();
            foreach (var method in _file.Descendants<IMethodDeclaration>())
            {
                if (DaemonProcess.InterruptFlag)
                    throw new OperationCanceledException();

                if (method.DeclaredElement is not IXmlDocIdOwner docOwner)
                    continue;

                Console.WriteLine($"[rig-spike] PSI method: {docOwner.XMLDocId}");
                if (!byDocId.TryGetValue(docOwner.XMLDocId, out var row))
                    continue;

                var range = method.NameIdentifier.GetDocumentRange();
                highlightings.Add(new HighlightingInfo(range, new RigEffectHighlighting(method, range, row)));
            }

            Console.WriteLine($"[rig-spike] committed {highlightings.Count} highlightings for {filePath}");
            committer(new DaemonStageResult(highlightings));
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
