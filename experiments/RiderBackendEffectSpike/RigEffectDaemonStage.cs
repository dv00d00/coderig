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
    private readonly FakeFileEffectHost _host;

    public RigEffectDaemonStage(IDaemon daemon)
    {
        Console.WriteLine("[rig-spike] daemon stage constructed");
        _host = new FakeFileEffectHost(daemon);
    }

    protected override IDaemonStageProcess CreateProcess(
        IDaemonProcess process,
        IContextBoundSettingsStore settings,
        DaemonProcessKind processKind,
        ICSharpFile file
    ) => new Process(process, file, _host);

    private sealed class Process : IDaemonStageProcess
    {
        private readonly ICSharpFile _file;
        private readonly FakeFileEffectHost _host;

        public Process(IDaemonProcess daemonProcess, ICSharpFile file, FakeFileEffectHost host)
        {
            DaemonProcess = daemonProcess;
            _file = file;
            _host = host;
        }

        public IDaemonProcess DaemonProcess { get; }

        public void Execute(Action<DaemonStageResult> committer)
        {
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
            if (!_host.TryGet(filePath, out var rows))
            {
                _host.Request(filePath);
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
    }
}
