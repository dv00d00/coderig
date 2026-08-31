using JetBrains.DocumentModel;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.TextControl.DocumentMarkup;

namespace CodeRig.Rider;

[RegisterHighlighter(
    SeverityId,
    Layer = (HighlighterLayer)2001,
    EffectType = EffectType.GUTTER_MARK,
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

    private readonly IMethodDeclaration _method;
    private readonly DocumentRange _range;

    public RigEffectHighlighting(IMethodDeclaration method, DocumentRange range, FileEffectRow row)
    {
        _method = method;
        _range = range;
        ToolTip = $"rig: reaches {row.Family} · nearest depth {row.NearestDepth}";
        ErrorStripeToolTip = ToolTip;
    }

    public string ToolTip { get; }

    public string ErrorStripeToolTip { get; }

    public bool IsValid() => _method.IsValid();

    public DocumentRange CalculateRange() => _range;
}
