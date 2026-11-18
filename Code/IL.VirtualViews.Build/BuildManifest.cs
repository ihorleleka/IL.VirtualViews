namespace IL.VirtualViews.Build;

/// <summary>Reads the typed path manifest passed by MSBuild without command-line length limits.</summary>
internal sealed class BuildManifest
{
    private readonly string[] lines;
    private readonly string projectDirectory;

    public BuildManifest(string path, string projectDirectory)
    {
        lines = File.ReadAllLines(path).Where(line => line.Length > 2 && line[1] == '|').ToArray();
        this.projectDirectory = projectDirectory;
    }

    public string[] Files(char kind) => lines.Where(line => line[0] == kind)
        .Select(line => Path.GetFullPath(line[2..], projectDirectory))
        .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        .Where(File.Exists).ToArray();
}
