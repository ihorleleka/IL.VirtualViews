using System.Xml.Linq;

namespace IL.VirtualViews.Build;

/// <summary>Keeps physical source paths separate from MVC's logical identifiers.</summary>
internal static class RazorInputManifest
{
    public static void Write(string output, IReadOnlyDictionary<string, string> originalSources,
        IReadOnlyDictionary<string, string> materializedSources)
    {
        var inputs = originalSources.Concat(materializedSources.Select(pair =>
            new KeyValuePair<string, string>(pair.Key, Path.Combine(output, pair.Key + ".cshtml"))));
        inputs = inputs.ToArray();
        var collision = inputs.GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (collision is not null)
            throw new InvalidOperationException($"Virtual view output names collide on case-insensitive file systems: {string.Join(", ", collision.Select(pair => pair.Key))}.");
        var document = new XDocument(new XElement("Project",
            new XElement("Target", new XAttribute("Name", "GetRazorInputs"),
                new XAttribute("Returns", "@(_VirtualViewRazorInput)"),
                new XElement("ItemGroup", inputs.Select(pair =>
                    new XElement("_VirtualViewRazorInput", new XAttribute("Include", Escape(pair.Value)),
                        new XElement("TargetPath", Escape("_VirtualViews/" + pair.Key + ".cshtml")),
                        new XElement("DocumentKind", "mvc")))))));
        var path = Path.Combine(output, "razor-inputs.proj");
        var content = document.ToString();
        if (!File.Exists(path) || File.ReadAllText(path) != content)
            File.WriteAllText(path, content);
    }

    private static string Escape(string value) => value.Replace("%", "%25").Replace("$", "%24")
        .Replace("@", "%40").Replace(";", "%3B").Replace("'", "%27")
        .Replace("(", "%28").Replace(")", "%29").Replace("*", "%2A").Replace("?", "%3F");
}
