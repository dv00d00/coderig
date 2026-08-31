using System.Collections.Generic;
using JetBrains.Application.UI.Controls.BulbMenu.Anchors;
using JetBrains.Application.UI.Controls.BulbMenu.Items;
using JetBrains.ReSharper.Feature.Services.Resources;
using JetBrains.TextControl.DocumentMarkup;
using JetBrains.Util;

namespace CodeRig.Rider;

internal sealed class RigEffectGutterMarkType : IconGutterMarkType
{
    public RigEffectGutterMarkType()
        : base(DaemonThemedIcons.Recursion.Id) { }

    public override IAnchor Priority => BulbMenuAnchors.PermanentBackgroundItems;

    public override IEnumerable<BulbMenuItem> GetBulbMenuItems(IHighlighter highlighter) =>
        EmptyList<BulbMenuItem>.Instance;
}
