namespace IL.VirtualViews.Tests.RazorRendering;

public class RazorRenderingTests
{
    [Theory]
    [InlineData("/views/debug-razor.cshtml", "<p>Hello &lt;world&gt;</p>")]
    [InlineData("/views/external.cshtml", "<p data-external=\"true\">Hello &lt;world&gt;</p>")]
    [InlineData("/views/test.cshtml", "<p>Test 123</p>")]
    public async Task Generated_derived_attribute_and_external_templates_render_without_source(string path, string expected)
    {
        using var provider = CompiledViewTestHost.Create();
        var rendered = await CompiledViewTestHost.RenderAsync(provider, path, "Hello <world>");
        Assert.Contains(expected, rendered);
    }

    [Theory]
    [InlineData("/views/record.cshtml", "<p>Record template</p>")]
    [InlineData("/_VirtualViews/custom.cshtml", "<code>/_VirtualViews/custom.cshtml</code>")]
    [InlineData("/_VirtualViews/physical.cshtml", "<p>Physical Razor view</p>")]
    public async Task Record_and_user_views_under_the_synthetic_prefix_remain_renderable(string path, string expected)
    {
        using var provider = CompiledViewTestHost.Create();
        var rendered = await CompiledViewTestHost.RenderAsync(provider, path, "ignored");
        Assert.Contains(expected, rendered);
    }

    [Fact]
    public async Task Escaped_identifier_template_exposes_the_registered_page_path()
    {
        using var provider = CompiledViewTestHost.Create();
        var rendered = await CompiledViewTestHost.RenderAsync(provider, "/views/escaped.cshtml", "ignored");
        Assert.Contains("<code>/views/escaped.cshtml</code>", rendered);
        Assert.DoesNotContain("/_VirtualViews/", rendered);
    }

    [Fact]
    public async Task Handwritten_constant_template_uses_the_consuming_assembly_model_identity()
    {
        using var provider = CompiledViewTestHost.Create();
        var model = new HandwrittenModel("A <typed> model");
        var compiler = provider.GetRequiredService<IViewCompilerProvider>().GetCompiler();
        var compiled = await compiler.CompileAsync("/views/handwritten.cshtml");
        Assert.NotNull(compiled.Item);
        Assert.Same(typeof(HandwrittenModel).Assembly, compiled.Item.Type.Assembly);
        Assert.Equal(typeof(HandwrittenModel), compiled.Item.Type.GetProperty("Model")!.PropertyType);
        var rendered = await CompiledViewTestHost.RenderAsync(provider, "/views/handwritten.cshtml", model);
        Assert.Contains("<strong>A &lt;typed&gt; model</strong>", rendered);
    }

    [Fact]
    public async Task Compiled_views_reuse_MVC_cache_but_render_fresh_models_concurrently()
    {
        using var provider = CompiledViewTestHost.Create();
        const string path = "/views/external.cshtml";
        var compiler = provider.GetRequiredService<IViewCompilerProvider>().GetCompiler();
        Assert.Equal("DefaultViewCompiler", compiler.GetType().Name);
        var compiled = await compiler.CompileAsync(path);
        Assert.NotNull(compiled.Item);

        foreach (var model in new[] { "First <model>", "Second & fresh model", "Third render" })
        {
            var rendered = await CompiledViewTestHost.RenderAsync(provider, path, model);
            Assert.Contains($"<p data-external=\"true\">{HtmlEncoder.Default.Encode(model)}</p>", rendered);
            Assert.Same(compiled, await compiler.CompileAsync(path));
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Task.Run(async () =>
        {
            var model = $"Concurrent <{index}>";
            var descriptor = await compiler.CompileAsync(path);
            var rendered = await CompiledViewTestHost.RenderAsync(provider, path, model);
            return (descriptor, rendered, model);
        })));
        Assert.All(results, result =>
        {
            Assert.Same(compiled, result.descriptor);
            Assert.Contains($"<p data-external=\"true\">{HtmlEncoder.Default.Encode(result.model)}</p>", result.rendered);
        });
    }

    [Fact]
    public async Task Compiled_layout_and_partial_resolve_registered_absolute_paths()
    {
        using var provider = CompiledViewTestHost.Create();
        var rendered = await CompiledViewTestHost.RenderAsync(provider, "/views/composed.cshtml", "Composed <model>");
        Assert.Contains("<main>", rendered);
        Assert.Contains("<article>Composed &lt;model&gt;</article>", rendered);
        Assert.Contains("<aside>Composed &lt;model&gt;</aside>", rendered);
        Assert.Contains("</main>", rendered);
    }
}
