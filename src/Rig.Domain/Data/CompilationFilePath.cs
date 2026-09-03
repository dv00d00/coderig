namespace Rig.Domain.Data;

// One canonical join key for persisted compilation-health paths. Stored/displayed paths stay untouched;
// only comparisons use this key so separator and platform-case differences cannot make a real compile
// error disappear at render time. Persisted analyzer/source-fact paths are expected to be absolute; this
// helper deliberately does not resolve relative paths against the ambient process working directory.
public static class CompilationFilePath
{
    public static StringComparer Comparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string Key(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        return path.Trim().Replace('\\', '/').TrimEnd('/');
    }

    public static bool Contains(IReadOnlySet<string>? keys, string? path) =>
        keys is not null && !string.IsNullOrEmpty(path) && keys.Contains(Key(path));
}
