using System.Text;

namespace Rig.Cli.Rendering;

internal static class TsvCell
{
    // TSV has no quoting layer in rig, so every cell must stay on one physical line and contain no tabs.
    // Collapse every whitespace run rather than inventing a second escaping convention.
    internal static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var sb = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
