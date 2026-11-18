using IL.VirtualViews.Tests.RazorRendering;

namespace IL.VirtualViews.Tests.ServiceCollection;

public class RegistrationTests
{
    [Fact]
    public void Registration_maps_compiled_items_to_attribute_paths_without_runtime_compilation()
    {
        using var provider = CompiledViewTestHost.Create();
        var manager = provider.GetRequiredService<ApplicationPartManager>();
        var feature = new ViewsFeature();
        manager.PopulateFeature(feature);

        var expectedPaths = new[]
        {
            "/views/debug-razor.cshtml", "/views/test.cshtml", "/views/external.cshtml",
            "/views/handwritten.cshtml", "/views/layout.cshtml", "/views/partial.cshtml",
            "/views/composed.cshtml", "/views/escaped.cshtml", "/views/record.cshtml",
            "/_VirtualViews/custom.cshtml", "/_VirtualViews/physical.cshtml"
        };
        foreach (var path in expectedPaths)
        {
            var descriptor = Assert.Single(feature.ViewDescriptors, item => item.RelativePath == path);
            Assert.NotNull(descriptor.Item);
            Assert.Equal(path, descriptor.Item.Identifier);
            Assert.Equal(typeof(DebugVirtualView).Assembly, descriptor.Item.Type.Assembly);
        }
        var virtualViewTypes = typeof(DebugVirtualView).Assembly.GetExportedTypes()
            .Where(type => typeof(IL.VirtualViews.Interfaces.IVirtualView).IsAssignableFrom(type));
        foreach (var type in virtualViewTypes)
        {
            var syntheticIdentifier = "/_VirtualViews/" + type.FullName + ".cshtml";
            Assert.DoesNotContain(feature.ViewDescriptors, item => item.RelativePath == syntheticIdentifier);
        }

        var rawItems = manager.ApplicationParts.OfType<IRazorCompiledItemProvider>()
            .SelectMany(part => part.CompiledItems);
        Assert.Contains(rawItems,
            item => item.Identifier == "/_VirtualViews/" + typeof(DebugVirtualView).FullName + ".cshtml");
        Assert.Equal("DefaultViewCompiler", provider.GetRequiredService<IViewCompilerProvider>().GetCompiler().GetType().Name);
        Assert.DoesNotContain(typeof(DebugVirtualView).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name == "Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation");
    }

    [Fact]
    public void Repeated_registration_is_idempotent_and_feature_population_is_repeatable()
    {
        using var provider = CompiledViewTestHost.Create(registerTwice: true);
        var manager = provider.GetRequiredService<ApplicationPartManager>();
        Assert.Single(manager.FeatureProviders,
            item => item.GetType().Name == "VirtualViewsFeatureProvider");
        Assert.Equal(manager.ApplicationParts.Count,
            manager.ApplicationParts.Select(part => (part.GetType(), part.Name)).Distinct().Count());
        var first = new ViewsFeature();
        var second = new ViewsFeature();
        manager.PopulateFeature(first);
        manager.PopulateFeature(second);
        Assert.Equal(first.ViewDescriptors.Select(item => item.RelativePath).OrderBy(path => path),
            second.ViewDescriptors.Select(item => item.RelativePath).OrderBy(path => path));
        Assert.Equal(first.ViewDescriptors.Count,
            first.ViewDescriptors.Select(item => item.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Production_host_has_no_template_source_file_provider()
    {
        using var provider = CompiledViewTestHost.Create();
        var environment = provider.GetRequiredService<IWebHostEnvironment>();
        Assert.Equal("Production", environment.EnvironmentName);
        Assert.IsType<NullFileProvider>(environment.ContentRootFileProvider);
        Assert.IsType<NullFileProvider>(environment.WebRootFileProvider);
        Assert.False(environment.ContentRootFileProvider.GetFileInfo("RazorRendering/DebugVirtualView.virtual.cshtml").Exists);
    }
}
