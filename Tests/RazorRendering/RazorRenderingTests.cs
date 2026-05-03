using System.Globalization;
using System.Diagnostics;
using IL.VirtualViews.ContentProvider;
using IL.VirtualViews.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Xunit;

namespace IL.VirtualViews.Tests.RazorRendering;

public class RazorRenderingTests
{
    [Fact]
    public async Task VirtualView_Should_Render_And_Expose_Debug_PhysicalPath_In_Development()
    {
        var previousEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");

        try
        {
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddSingleton<IWebHostEnvironment>(new MockWebHostEnvironment());
            services.AddSingleton<DiagnosticListener>(_ => new DiagnosticListener("IL.VirtualViews.Tests"));
            services.AddSingleton<DiagnosticSource>(provider => provider.GetRequiredService<DiagnosticListener>());
            services.AddLogging();
            services.AddVirtualViewsCapabilities("IL.VirtualViews.Tests");

            var serviceProvider = services.BuildServiceProvider();

            var options = serviceProvider.GetRequiredService<IOptions<MvcRazorRuntimeCompilationOptions>>();
            var virtualProvider = options.Value.FileProviders
                .OfType<VirtualViewsProvider>()
                .First();

            var fileInfo = virtualProvider.GetFileInfo("/views/debug-razor.cshtml");
            Assert.True(fileInfo.Exists);
            Assert.False(string.IsNullOrWhiteSpace(fileInfo.PhysicalPath));
            Assert.True(File.Exists(fileInfo.PhysicalPath));

            var rendered = await RenderAsync(serviceProvider, "/views/debug-razor.cshtml", "Hello");
            Assert.Contains("<p>Hello</p>", rendered);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", previousEnvironment);
        }
    }

    [Fact]
    public async Task DerivedVirtualViewAttribute_Should_Register_And_Render()
    {
        var previousEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");

        try
        {
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddSingleton<IWebHostEnvironment>(new MockWebHostEnvironment());
            services.AddSingleton<DiagnosticListener>(_ => new DiagnosticListener("IL.VirtualViews.Tests"));
            services.AddSingleton<DiagnosticSource>(provider => provider.GetRequiredService<DiagnosticListener>());
            services.AddLogging();
            services.AddVirtualViewsCapabilities("IL.VirtualViews.Tests");

            var serviceProvider = services.BuildServiceProvider();
            var options = serviceProvider.GetRequiredService<IOptions<MvcRazorRuntimeCompilationOptions>>();
            var virtualProvider = options.Value.FileProviders
                .OfType<VirtualViewsProvider>()
                .First();

            var fileInfo = virtualProvider.GetFileInfo("/views/test.cshtml");
            Assert.True(fileInfo.Exists);
            Assert.False(string.IsNullOrWhiteSpace(fileInfo.PhysicalPath));
            Assert.True(File.Exists(fileInfo.PhysicalPath));

            var rendered = await RenderAsync(serviceProvider, "/views/test.cshtml", "ignored");
            Assert.Contains("<p>Test 123</p>", rendered);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", previousEnvironment);
        }
    }

    private static async Task<string> RenderAsync(IServiceProvider serviceProvider, string viewPath, object model)
    {
        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        var razorViewEngine = serviceProvider.GetRequiredService<IRazorViewEngine>();
        var tempDataProvider = serviceProvider.GetRequiredService<ITempDataProvider>();

        var viewResult = razorViewEngine.GetView(executingFilePath: null, viewPath: viewPath, isMainPage: true);
        Assert.True(viewResult.Success);

        await using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };

        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewData,
            new TempDataDictionary(httpContext, tempDataProvider),
            writer,
            new HtmlHelperOptions()
        );

        await viewResult.View.RenderAsync(viewContext);
        return writer.ToString();
    }

    private class MockWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "IL.VirtualViews.Tests";
        public string WebRootPath { get; set; } = "wwwroot";
        public string ContentRootPath { get; set; } = "ContentRoot";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
