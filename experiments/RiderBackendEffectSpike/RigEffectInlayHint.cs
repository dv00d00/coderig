using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Application.Parts;
using JetBrains.Application.UI.Controls.BulbMenu.Items;
using JetBrains.Application.UI.Controls.Utils;
using JetBrains.Application.UI.PopupLayout;
using JetBrains.DocumentModel;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Feature.Services.InlayHints;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.TextControl.DocumentMarkup;
using JetBrains.TextControl.DocumentMarkup.Adornments;
using JetBrains.UI.RichText;
using JetBrains.Util;

namespace CodeRig.Rider;

/// <summary>
/// EXPERIMENT (2026-08-31): the second rendering arm. A text attribute alone proved invisible in Rider, so
/// this projects the same call-site row as an INTRA-TEXT ADORNMENT — the mechanism behind Rider's parameter
/// name / type hints — rendered immediately after the invoked name as `sql·N`.
/// </summary>
[DaemonAdornmentProvider(typeof(RigEffectInlayAdornmentProvider))]
[StaticSeverityHighlighting(
    Severity.INFO,
    typeof(HighlightingGroupIds.IntraTextAdornments),
    AttributeId = "ReSharper Parameter Name Hint",
    OverlapResolve = OverlapResolveKind.NONE,
    ShowToolTipInStatusBar = false
)]
internal sealed class RigEffectInlayHighlighting : IInlayHintWithDescriptionHighlighting
{
    private readonly ITreeNode _anchor;
    private readonly DocumentRange _range;

    public RigEffectInlayHighlighting(ITreeNode anchor, DocumentRange range, IReadOnlyList<FileEffectCallSiteRow> rows)
    {
        _anchor = anchor;
        _range = range;
        var orderedRows = rows.OrderBy(row => RigEffectFamilyStyle.Rank(row.Family)).ToArray();
        HintText = " " + string.Join(" ", orderedRows.Select(row => RigEffectFamilyStyle.Label(row.Family, row.NearestDepth))) + " ";

        // The adornment renders RICH text, so each family carries its own colour. HintText above stays the
        // plain equivalent: it is what the IInlayHintHighlighting contract exposes and what the tooltip and
        // any log line read.
        RichHint = new RichText(" ", TextStyle.Default);
        foreach (var row in orderedRows)
        {
            var style = RigEffectFamilyStyle.Style(row.Family, row.NearestDepth);
            RichHint.Append(RigEffectFamilyStyle.Label(row.Family, row.NearestDepth), style);
            RichHint.Append(" ", style);
        }

        ToolTip =
            "rig: "
            + string.Join(
                "; ",
                orderedRows.Select(row =>
                    row.NearestDepth == 0
                        ? $"this call performs a {row.Family} effect"
                        : $"this call reaches {row.Family} · remaining depth {row.NearestDepth}"
                )
            );
        ErrorStripeToolTip = ToolTip;
    }

    public string HintText { get; }

    // The coloured form of HintText, which is what actually reaches the editor.
    public RichText RichHint { get; }

    public RichText Description => new RichText(ToolTip);

    public string ToolTip { get; }

    public string ErrorStripeToolTip { get; }

    public bool IsValid() => _anchor.IsValid();

    public DocumentRange CalculateRange() => _range;
}

[SolutionComponent(Instantiation.DemandAnyThreadSafe)]
internal sealed class RigEffectInlayAdornmentProvider : IHighlighterAdornmentProvider
{
    public bool IsValid(IHighlighter highlighter) => highlighter.GetHighlighting() is RigEffectInlayHighlighting hint && hint.IsValid();

    public IAdornmentDataModel CreateDataModel(IHighlighter highlighter) =>
        highlighter.GetHighlighting() is RigEffectInlayHighlighting hint && hint.IsValid() ? new RigEffectInlayDataModel(hint) : null;
}

internal sealed class RigEffectInlayDataModel : IAdornmentDataModel
{
    public RigEffectInlayDataModel(RigEffectInlayHighlighting hint)
    {
        Data = new AdornmentData(
            hint.RichHint,
            icon: null,
            AdornmentFlags.None,
            new AdornmentPlacement { Position = AdornmentPosition.INLINE, Priority = 0 },
            PushToHintMode.Default
        );
    }

    public AdornmentData Data { get; }

    public IPresentableItem ContextMenuTitle => null;

    public IEnumerable<BulbMenuItem> ContextMenuItems => null;

    public TextRange? SelectionRange => null;

    public void ExecuteNavigation(PopupWindowContextSource popupWindowContextSource) { }
}
