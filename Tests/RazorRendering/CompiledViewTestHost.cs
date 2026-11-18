namespace IL.VirtualViews.Tests.RazorRendering;

internal static class CompiledViewTestHost
{
    internal static ServiceProvider Create(bool registerTwice = false)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<IWebHostEnvironment>(new ProductionEnvironment());
        services.AddSingleton<DiagnosticListener>(_ => new DiagnosticListener("IL.VirtualViews.Tests"));
        services.AddSingleton<DiagnosticSource>(provider => provider.GetRequiredService<DiagnosticListener>());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddVirtualViewsCapabilities("IL.VirtualViews.Tests");
        if (registerTwice)
            services.AddVirtualViewsCapabilities("IL.VirtualViews.Tests");
        return services.BuildServiceProvider();
    }

    internal static async Task<string> RenderAsync(IServiceProvider provider, string path, object model)
    {
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var result = services.GetRequiredService<IRazorViewEngine>().GetView(null, path, isMainPage: true);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.SearchedLocations ?? []));
        await using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };
        var viewContext = new ViewContext(actionContext, result.View, viewData,
            new TempDataDictionary(httpContext, services.GetRequiredService<ITempDataProvider>()),
            writer, new HtmlHelperOptions());
        await result.View.RenderAsync(viewContext);
        return writer.ToString();
    }

    private sealed class ProductionEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = typeof(CompiledViewTestHost).Assembly.GetName().Name!;
        public string WebRootPath { get; set; } = string.Empty;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
