using JetBrains.DocumentModel;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.Tree;

namespace CodeRig.Rider;

/// <summary>
/// Why rig has NO effect data for the open file, as one row in Problems.
///
/// The absence of marks used to be indistinguishable from "there are no effects here": a file that did not
/// compile, a file no project indexes, a file indexed twice and a dead resident host all rendered as the same
/// empty screen. Only causes the host scopes to THIS FILE reach this highlighting — a host-scoped cause
/// (booting, reconciling, topology changed) belongs to the status widget, because a warning per open document
/// during a 148s MedDBase cold boot is noise that trains the reader to ignore the surface.
///
/// Registered as a CONFIGURABLE severity, so it is mutable (or mutable to "Do not show") in
/// Inspection Settings like any other inspection, rather than being a mark the user cannot turn off.
/// </summary>
[RegisterConfigurableSeverity(
    SeverityId,
    null,
    HighlightingGroupIds.CodeInfo,
    "CodeRig has no effect data for this file",
    "rig could not answer for this file — it did not compile when it was indexed, no project indexes it, or "
        + "several projects claim it. The effect marks are absent for that reason, not because the file is free "
        + "of effects.",
    Severity.WARNING
)]
[ConfigurableSeverityHighlighting(SeverityId, CSharpLanguage.Name, OverlapResolve = OverlapResolveKind.NONE)]
internal sealed class RigCoverageHighlighting : IHighlighting
{
    public const string SeverityId = "RigFileEffectsUnavailable";

    private readonly IFile _file;
    private readonly DocumentRange _range;

    public RigCoverageHighlighting(IFile file, DocumentRange range, string reasonCode, string reason)
    {
        _file = file;
        _range = range;
        ToolTip = $"rig: no effect data for this file ({reasonCode}) — {reason}";
        ErrorStripeToolTip = ToolTip;
    }

    public string ToolTip { get; }

    public string ErrorStripeToolTip { get; }

    public bool IsValid() => _file.IsValid();

    public DocumentRange CalculateRange() => _range;
}
