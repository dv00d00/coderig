using System;
using System.Collections.Generic;
using JetBrains.Application.Settings;
using JetBrains.ReSharper.Daemon.CodeInsights;
using JetBrains.ReSharper.Feature.Services.CSharp.Daemon;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Feature.Services.Resources;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.Rider.Backend.Platform.Icons;
using JetBrains.Rider.Model;

namespace CodeRig.Rider;

[DaemonStage]
internal sealed class RigEffectDaemonStage : CSharpDaemonStageBase
{
    private readonly RigFileEffectHost _host;
    private readonly RigEffectCodeInsightsProvider _codeInsightsProvider;
    private readonly IconModel _codeInsightsIcon;

    public RigEffectDaemonStage(
        IDaemon daemon,
        RigEffectCodeInsightsProvider codeInsightsProvider,
        IconHost iconHost
    )
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
    ) =>
        new Process(
            process,
            file,
            _host,
            _codeInsightsProvider,
            _codeInsightsIcon,
            processKind == DaemonProcessKind.VISIBLE_DOCUMENT
        );

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

                if (!byDocId.TryGetValue(docOwner.XMLDocId, out var row))
                    continue;

                var range = method.NameIdentifier.GetDocumentRange();
                highlightings.Add(new HighlightingInfo(range, new RigEffectHighlighting(method, range, row)));
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
            }

            if (highlightings.Count > 0)
                Console.WriteLine(
                    $"[CodeRig Rider] projected methods={highlightings.Count / 2}, "
                        + $"uiHighlightings={highlightings.Count}, file={filePath}"
                );
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
