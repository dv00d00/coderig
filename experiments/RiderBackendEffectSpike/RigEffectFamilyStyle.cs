using System;
using JetBrains.UI.RichText;
using JetBrains.Util;
using JetBrains.Util.Media;

namespace CodeRig.Rider;

// One table for how a FAMILY reads in the editor: its order, its glyph and its colour. The families
// themselves are rules vocabulary (the host derives them from `providers` in rig.rules.json), so this table
// deliberately holds no opinion about which families exist — an unknown one gets the neutral entry rather
// than being dropped or renamed.
//
// The colours are single mid-luminance values, not light/dark pairs: an intra-text adornment carries its own
// RichText, so it never picks up a themed highlighter attribute. They are chosen to stay legible on both
// grounds; a per-theme pair would need seven registered highlighter attributes and `TextStyle(attributeId)`.
internal static class RigEffectFamilyStyle
{
    private static readonly (string Family, string Glyph, JetRgbaColor Color)[] Families =
    {
        ("db", "▤", JetRgbaColor.FromRgb(0x3C, 0x7E, 0xB8)),
        ("cache", "⧉", JetRgbaColor.FromRgb(0x2E, 0x9E, 0x6B)),
        ("echo", "⇉", JetRgbaColor.FromRgb(0x8A, 0x5C, 0xD6)),
        ("bus", "⇉", JetRgbaColor.FromRgb(0x8A, 0x5C, 0xD6)),
        ("rpc", "⇄", JetRgbaColor.FromRgb(0xCB, 0x7A, 0x22)),
        ("search", "⌕", JetRgbaColor.FromRgb(0x2F, 0x8F, 0x9E)),
        ("blob", "▨", JetRgbaColor.FromRgb(0xA0, 0x6B, 0x3F)),
        ("io", "▭", JetRgbaColor.FromRgb(0x6E, 0x76, 0x80)),
        ("file", "▭", JetRgbaColor.FromRgb(0x6E, 0x76, 0x80)),
    };

    private const string UnknownGlyph = "◆";

    // Prefixed to a depth-0 row: the effect is right here, not N calls away.
    private const string HereMarker = "●";

    private static readonly JetRgbaColor UnknownColor = JetRgbaColor.FromRgb(0x87, 0x87, 0x87);

    // Rank first by the table (the reading order: storage, then messaging, then transport, then bytes), and
    // put anything undeclared last so a new provider family appears rather than jumping the queue.
    public static int Rank(string family)
    {
        var index = IndexOf(family);
        return index < 0 ? Families.Length : index;
    }

    public static string Glyph(string family)
    {
        var index = IndexOf(family);
        return index < 0 ? UnknownGlyph : Families[index].Glyph;
    }

    public static TextStyle Style(string family) => Style(family, 1);

    // DEPTH 0 is not "close to an effect", it IS the effect — this method (or this expression) performs it.
    // Everything else is a distance. So depth 0 gets the filled marker and bold, and drops the `·0` that
    // otherwise reads as just another number.
    public static TextStyle Style(string family, int nearestDepth)
    {
        var index = IndexOf(family);
        var color = index < 0 ? UnknownColor : Families[index].Color;
        return nearestDepth == 0 ? new TextStyle(JetFontStyles.Bold, color) : TextStyle.FromForeColor(color);
    }

    public static string Label(string family, int nearestDepth) =>
        nearestDepth == 0 ? HereMarker + Glyph(family) + family : Glyph(family) + family + "·" + nearestDepth;

    private static int IndexOf(string family)
    {
        for (var i = 0; i < Families.Length; i++)
        {
            if (string.Equals(Families[i].Family, family, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
}
