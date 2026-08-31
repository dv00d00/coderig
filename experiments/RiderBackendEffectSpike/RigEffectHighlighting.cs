using JetBrains.DocumentModel;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.CSharp.Tree;

namespace RiderBackendEffectSpike;

[RegisterConfigurableSeverity(
    SeverityId,
    CompoundItemName: null,
    CompoundItemNameResourceType: null,
    CompoundItemNameResourceName: null,
    Group: HighlightingGroupIds.CodeInfo,
    Title: "rig reachable effect",
    TitleResourceType: null,
    TitleResourceName: null,
    Description: "A method reaches an effect reported by the rig index.",
    DescriptionResourceType: null,
    DescriptionResourceName: null,
    DefaultSeverity: Severity.INFO
)]
[ConfigurableSeverityHighlighting(SeverityId, CSharpLanguage.Name, OverlapResolve = OverlapResolveKind.NONE)]
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
