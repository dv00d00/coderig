using System.Drawing;
using JetBrains.DocumentModel;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.TextControl.DocumentMarkup;

namespace CodeRig.Rider;

// EXPERIMENT (2026-08-31): the call-site style. Bold alone was indistinguishable from ordinary method
// colouring, so this arm adds an explicit foreground per theme plus a solid underline in the same colour.
// Revert to `EffectType.GUTTER_MARK | EffectType.TEXT` + bare Bold if it reads as noise.
[RegisterHighlighter(
    RigSqlEffectHighlighting.SeverityId,
    Layer = (HighlighterLayer)2001,
    EffectType = EffectType.GUTTER_MARK | EffectType.TEXT | EffectType.SOLID_UNDERLINE,
    FontStyle = FontStyle.Bold,
    ForegroundColor = "#8C4B00",
    DarkForegroundColor = "#E8A33D",
    EffectColor = "#8C4B00",
    GutterMarkType = typeof(RigSqlEffectGutterMarkType)
)]
// EXPERIMENT arm 2: the group. GutterMarks got the gutter icon rendered but no text attribute reached the
// Rider frontend; IdentifierHighlightings is the group semantic identifier colours travel in.
[StaticSeverityHighlighting(
    Severity.INFO,
    typeof(HighlightingGroupIds.IdentifierHighlightings),
    OverlapResolve = OverlapResolveKind.NONE,
    AttributeId = RigSqlEffectHighlighting.SeverityId,
    ShowToolTipInStatusBar = false
)]
internal sealed class RigSqlEffectHighlighting : RigEffectHighlighting
{
    public const string SeverityId = "RigReachableSqlEffect";

    public RigSqlEffectHighlighting(IInvocationExpression invocation, DocumentRange range, FileEffectCallSiteRow row)
        : base(invocation, range, row) { }
}

[RegisterHighlighter(
    RigFileEffectHighlighting.SeverityId,
    Layer = (HighlighterLayer)2001,
    EffectType = EffectType.GUTTER_MARK | EffectType.TEXT | EffectType.SOLID_UNDERLINE,
    FontStyle = FontStyle.Bold,
    ForegroundColor = "#315E9D",
    DarkForegroundColor = "#6FA8EF",
    EffectColor = "#315E9D",
    GutterMarkType = typeof(RigFileEffectGutterMarkType)
)]
[StaticSeverityHighlighting(
    Severity.INFO,
    typeof(HighlightingGroupIds.IdentifierHighlightings),
    OverlapResolve = OverlapResolveKind.NONE,
    AttributeId = RigFileEffectHighlighting.SeverityId,
    ShowToolTipInStatusBar = false
)]
internal sealed class RigFileEffectHighlighting : RigEffectHighlighting
{
    public const string SeverityId = "RigReachableFileEffect";

    public RigFileEffectHighlighting(IInvocationExpression invocation, DocumentRange range, FileEffectCallSiteRow row)
        : base(invocation, range, row) { }
}

internal abstract class RigEffectHighlighting : IHighlighting
{
    private readonly IInvocationExpression _invocation;
    private readonly DocumentRange _range;

    protected RigEffectHighlighting(IInvocationExpression invocation, DocumentRange range, FileEffectCallSiteRow row)
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

    public static RigEffectHighlighting Create(IInvocationExpression invocation, DocumentRange range, FileEffectCallSiteRow row) =>
        string.Equals(row.Family, "file", System.StringComparison.Ordinal)
            ? new RigFileEffectHighlighting(invocation, range, row)
            : new RigSqlEffectHighlighting(invocation, range, row);
}
