using IL.VirtualViews.Attributes;
using IL.VirtualViews.ContentProvider;
using IL.VirtualViews.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Xunit;

namespace IL.VirtualViews.Tests.ServiceCollection;

public class RegistrationTests
{
    [Fact]
    public void RegistrationShouldIntroduceNewVirtualViewsInMentionedPaths()
    {
        var serviceCollection = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        serviceCollection.AddSingleton<IWebHostEnvironment>(new MockWebHostEnvironment());
        serviceCollection.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        serviceCollection.AddVirtualViewsCapabilities();
        var serviceProvider = serviceCollection.BuildServiceProvider();
        // Act
        var options = serviceProvider.GetService<IOptions<MvcRazorRuntimeCompilationOptions>>();

        // Assert
        Assert.NotNull(options);
        var fileProviders = options.Value.FileProviders;
        Assert.NotEmpty(fileProviders);

        var virtualViewsProvider = fileProviders.OfType<VirtualViewsProvider>().FirstOrDefault();
        Assert.NotNull(virtualViewsProvider);
        
        Assert.NotNull(virtualViewsProvider.GetFileInfo("/views/test.cshtml"));
    }
    
    // Simple mock implementation of IWebHostEnvironment
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