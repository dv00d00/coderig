using System.Collections.Generic;
using JetBrains.Application.Parts;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Daemon.CodeInsights;
using JetBrains.Rider.Model;

namespace CodeRig.Rider;

[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
internal sealed class RigEffectCodeInsightsProvider : ICodeInsightsProvider
{
    public string ProviderId => nameof(RigEffectCodeInsightsProvider);

    public string DisplayName => "CodeRig SQL effects";

    public CodeVisionAnchorKind DefaultAnchor => CodeVisionAnchorKind.Top;

    public ICollection<CodeVisionRelativeOrdering> RelativeOrderings { get; } =
        new List<CodeVisionRelativeOrdering> { new CodeVisionRelativeOrderingFirst() };

    public bool IsAvailableIn(ISolution solution) => true;

    public void OnClick(
        CodeInsightHighlightInfo highlightInfo,
        ISolution solution,
        CodeInsightsClickInfo clickInfo
    ) { }

    public void OnExtraActionClick(CodeInsightHighlightInfo highlightInfo, string actionId, ISolution solution) { }
}
