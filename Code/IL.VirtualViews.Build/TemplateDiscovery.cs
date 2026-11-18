namespace IL.VirtualViews.Build;

/// <summary>Maps physical AdditionalFiles to their historical VirtualViews class identity.</summary>
internal static class TemplateDiscovery
{
    public static Dictionary<string, string> Find(IEnumerable<string> files, string assembly)
    {
        var templates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Where(path => path.EndsWith(".virtual.cshtml", StringComparison.OrdinalIgnoreCase)))
        {
            // Preserve the historical namespace contract: last assembly-prefix folder,
            // then every directory segment from there, otherwise the assembly name.
            var parts = Path.GetDirectoryName(file)!.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            var start = Array.FindLastIndex(parts, part => assembly.StartsWith(part, StringComparison.InvariantCultureIgnoreCase));
            var ns = start >= 0 ? string.Join('.', parts.Skip(start)) : assembly;
            var name = Path.GetFileName(file)[..^".virtual.cshtml".Length];
            if (!templates.TryAdd(ns + "." + name, file))
                throw new InvalidOperationException($"Multiple .virtual.cshtml files map to '{ns}.{name}'.");
        }
        return templates;
    }
}
