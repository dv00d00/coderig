using System.Drawing;
using JetBrains.DocumentModel;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.TextControl.DocumentMarkup;

namespace CodeRig.Rider;

[RegisterHighlighter(
    SeverityId,
    Layer = (HighlighterLayer)2001,
    EffectType = EffectType.GUTTER_MARK | EffectType.TEXT,
    FontStyle = FontStyle.Bold,
    GutterMarkType = typeof(RigEffectGutterMarkType)
)]
[StaticSeverityHighlighting(
    Severity.INFO,
    typeof(HighlightingGroupIds.GutterMarks),
    OverlapResolve = OverlapResolveKind.NONE,
    AttributeId = SeverityId,
    ShowToolTipInStatusBar = false
)]
internal sealed class RigEffectHighlighting : IHighlighting
{
    public const string SeverityId = "RigReachableEffect";

    private readonly IInvocationExpression _invocation;
    private readonly DocumentRange _range;

    public RigEffectHighlighting(IInvocationExpression invocation, DocumentRange range, FileEffectCallSiteRow row)
    {
        _invocation = invocation;
        _range = range;
        ToolTip = $"rig: this call reaches {row.Family} · remaining depth {row.NearestDepth}";
        ErrorStripeToolTip = ToolTip;
    }

    public string ToolTip { get; }

    public string ErrorStripeToolTip { get; }

    public bool IsValid() => _invocation.IsValid();

    public DocumentRange CalculateRange() => _range;
}
