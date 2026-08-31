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
    private readonly IInvocationExpression _invocation;
    private readonly DocumentRange _range;

    public RigEffectInlayHighlighting(IInvocationExpression invocation, DocumentRange range, IReadOnlyList<FileEffectCallSiteRow> rows)
    {
        _invocation = invocation;
        _range = range;
        var orderedRows = rows.OrderBy(row => string.Equals(row.Family, "sql", StringComparison.Ordinal) ? 0 : 1).ToArray();
        HintText = " " + string.Join(" ", orderedRows.Select(row => $"{row.Family}·{row.NearestDepth}")) + " ";
        ToolTip =
            "rig: " + string.Join("; ", orderedRows.Select(row => $"this call reaches {row.Family} · remaining depth {row.NearestDepth}"));
        ErrorStripeToolTip = ToolTip;
    }

    public string HintText { get; }

    public RichText Description => new RichText(ToolTip);

    public string ToolTip { get; }

    public string ErrorStripeToolTip { get; }

    public bool IsValid() => _invocation.IsValid();

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
            new RichText(hint.HintText),
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
