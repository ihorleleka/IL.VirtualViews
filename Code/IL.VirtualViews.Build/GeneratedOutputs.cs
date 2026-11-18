using System.Text;

namespace IL.VirtualViews.Build;

/// <summary>Owns incremental writes and manifest-scoped stale output cleanup.</summary>
internal static class GeneratedOutputs
{
    public static void Write(string output, string manifestName, string extension, IReadOnlyDictionary<string, string> contents)
    {
        var collision = contents.Keys.GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (collision is not null)
            throw new InvalidOperationException($"Virtual view output names collide on case-insensitive file systems: {string.Join(", ", collision)}. Rename one of the view classes/templates.");
        Directory.CreateDirectory(output);
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var written = new List<string>();
        foreach (var name in contents.Keys)
            written.Add(Path.Combine(output, name + extension));
        var manifest = Path.Combine(output, manifestName);
        if (File.Exists(manifest))
            foreach (var old in File.ReadAllLines(manifest))
            {
                var full = Path.GetFullPath(old);
                // Never remove arbitrary wildcard matches or paths outside the exact
                // output directory. Each manifest owns only its designated extension.
                if (pathComparer.Equals(Path.GetDirectoryName(full), output)
                    && full.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                    && !written.Contains(full, pathComparer) && File.Exists(full))
                    File.Delete(full);
            }
        // Clean before writing: a case-only rename may alias the old file on
        // macOS's case-insensitive volumes, but be a distinct file on Linux.
        foreach (var (name, content) in contents)
            WriteChanged(Path.Combine(output, name + extension), content);
        WriteChanged(manifest, string.Join(Environment.NewLine, written) + (written.Count > 0 ? Environment.NewLine : ""));
    }

    private static void WriteChanged(string path, string content)
    {
        if (!File.Exists(path) || File.ReadAllText(path) != content)
            File.WriteAllText(path, content, new UTF8Encoding(false));
    }
}
