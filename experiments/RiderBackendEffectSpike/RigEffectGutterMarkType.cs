using System.Collections.Generic;
using JetBrains.Application.Resources;
using JetBrains.Application.UI.Controls.BulbMenu.Anchors;
using JetBrains.Application.UI.Controls.BulbMenu.Items;
using JetBrains.TextControl.DocumentMarkup;
using JetBrains.UI.ThemedIcons;
using JetBrains.Util;

namespace CodeRig.Rider;

internal sealed class RigSqlEffectGutterMarkType : IconGutterMarkType
{
    public RigSqlEffectGutterMarkType()
        : base(DatabasesThemedIcons.Query.Id) { }

    public override IAnchor Priority => BulbMenuAnchors.PermanentBackgroundItems;

    public override IEnumerable<BulbMenuItem> GetBulbMenuItems(IHighlighter highlighter) => EmptyList<BulbMenuItem>.Instance;
}

internal sealed class RigFileEffectGutterMarkType : IconGutterMarkType
{
    public RigFileEffectGutterMarkType()
        : base(IdeThemedIcons.FolderOpened.Id) { }

    public override IAnchor Priority => BulbMenuAnchors.PermanentBackgroundItems;

    public override IEnumerable<BulbMenuItem> GetBulbMenuItems(IHighlighter highlighter) => EmptyList<BulbMenuItem>.Instance;
}
