using System.Reflection;
using IL.VirtualViews.Attributes;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor.Compilation;
using Microsoft.AspNetCore.Razor.Hosting;

namespace IL.VirtualViews.Extensions;

// Runs after MVC's compiled-item provider. No template text is evaluated here.
internal sealed class VirtualViewsFeatureProvider : IApplicationFeatureProvider<ViewsFeature>
{
    private readonly HashSet<Type> _types = [];

    internal void AddTypes(IEnumerable<Type> types) => _types.UnionWith(types);

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ViewsFeature feature)
    {
        var compiledItems = parts.OfType<IRazorCompiledItemProvider>()
            .SelectMany(part => part.CompiledItems).ToList();
        // Remove only raw generated descriptors before adding public mappings, so a
        // user may legitimately register a view beneath /_VirtualViews/ as well.
        var expectedIdentifiers = _types.Select(type => (type.Assembly, Identifier: "/_VirtualViews/" + type.FullName + ".cshtml")).ToHashSet();
        var syntheticItems = compiledItems.Where(item => expectedIdentifiers.Contains((item.Type.Assembly, item.Identifier)))
            .Select(item => (item.Type, item.Identifier)).ToHashSet();
        for (var i = feature.ViewDescriptors.Count - 1; i >= 0; i--)
            if (feature.ViewDescriptors[i].Item is { } item && syntheticItems.Contains((item.Type, item.Identifier)))
                feature.ViewDescriptors.RemoveAt(i);
        var registeredPaths = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in _types)
        {
            var path = type.GetCustomAttributes<VirtualViewPathAttribute>(inherit: true).First().Path;
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/') || !path.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)
                || path.Contains('\\') || path.Split('/').Any(segment => segment is ".." or "."))
                throw new InvalidOperationException($"Virtual view '{type.FullName}' must have an absolute application-relative .cshtml path without traversal segments.");
            if (!registeredPaths.TryAdd(path, type))
                throw new InvalidOperationException($"Virtual view path '{path}' is registered by both '{registeredPaths[path]}' and '{type}'.");
            var identifier = "/_VirtualViews/" + type.FullName + ".cshtml";
            var item = compiledItems.SingleOrDefault(candidate => candidate.Identifier == identifier && candidate.Type.Assembly == type.Assembly);
            if (item == null)
                throw new InvalidOperationException($"Virtual view '{type.FullName}' has no build-time compiled Razor template. Restore the IL.VirtualViews package build assets and rebuild the declaring project. Razor build-time compilation must remain enabled; dynamic ViewContent is not supported.");
            var existing = feature.ViewDescriptors.FirstOrDefault(view => string.Equals(view.RelativePath, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                throw new InvalidOperationException($"Virtual view path '{path}' conflicts with an existing compiled Razor view.");
            feature.ViewDescriptors.Add(new CompiledViewDescriptor(new MappedCompiledItem(item, path)));
        }
    }

    private sealed class MappedCompiledItem(RazorCompiledItem source, string path) : RazorCompiledItem
    {
        public override string Identifier => path;
        public override string Kind => source.Kind;
        public override Type Type => source.Type;
        public override IReadOnlyList<object> Metadata => source.Metadata;
    }
}
