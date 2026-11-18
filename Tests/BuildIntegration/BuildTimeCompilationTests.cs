namespace IL.VirtualViews.Tests.BuildIntegration;

public class BuildTimeCompilationTests
{
    // One isolated package/consumer lifecycle per test assembly/TFM. Nothing writes
    // into repository bin/obj or a developer's global NuGet package cache.
    [Fact]
    public async Task Packed_package_compiles_renders_and_incrementally_updates_plain_sdk_consumer()
    {
        var root = Path.Combine(Path.GetTempPath(), "IL.VirtualViews buildtime " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var framework = typeof(BuildTimeCompilationTests).Assembly
            .GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
            .Cast<TargetFrameworkAttribute>().Single().FrameworkName;
        var tfm = "net" + framework.Split("Version=v", StringSplitOptions.None)[1];
        const string version = "0.0.0-buildtime-test";
        var feed = Path.Combine(root, "feed");
        Directory.CreateDirectory(feed);
        // MSBuild stages the package beside the test assembly. Tests have no
        // knowledge of checkout paths and never discover or build source projects.
        var packages = Path.Combine(AppContext.BaseDirectory, "TestAssets", "Packages");
        Assert.True(Directory.Exists(packages), "Build must stage TestAssets/Packages beside the test assembly.");
        foreach (var package in Directory.GetFiles(packages, "*.nupkg"))
            File.Copy(package, Path.Combine(feed, Path.GetFileName(package)));
        Assert.Single(Directory.GetFiles(feed, "IL.VirtualViews.*.nupkg"));
        var config = Path.Combine(root, "NuGet.Config");
        await File.WriteAllTextAsync(config, $"""
            <configuration>
              <packageSources><clear />
                <add key="local" value="{SecurityElement.Escape(feed)}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <fallbackPackageFolders><clear /></fallbackPackageFolders>
            </configuration>
            """);
        var runner = new DotNet(root);

        var consumer = Path.Combine(root, "consumer");
        Directory.CreateDirectory(consumer);
        var project = Path.Combine(consumer, "Consumer.csproj");
        await File.WriteAllTextAsync(project, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{{tfm}}</TargetFramework><OutputType>Exe</OutputType>
                <AssemblyName>PackageConsumer</AssemblyName><ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
                <PackageReference Include="IL.VirtualViews" Version="[{{version}}]" />
                <AdditionalFiles Include="../lInKeD.VIRTUAL.CSHTML" Link="lInKeD.VIRTUAL.CSHTML"
                                 Condition="Exists('../lInKeD.VIRTUAL.CSHTML')" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(consumer, "Program.cs"), RuntimeProgram);
        var view = Path.Combine(consumer, "View.cs");
        const string linkedView = """
            using IL.VirtualViews.Attributes;
            namespace PackageConsumer;
            [VirtualViewPath("/views/package.cshtml")]
            public partial class Linked;
            """;
        await File.WriteAllTextAsync(view, linkedView);
        var template = Path.Combine(root, "lInKeD.VIRTUAL.CSHTML");
        await File.WriteAllTextAsync(template, "@model string\n<p data-version=\"first\">@Model</p>");
        await runner.Run("restore", project, "--configfile", config);
        var assets = await File.ReadAllTextAsync(Path.Combine(consumer, "obj", "project.assets.json"));
        Assert.DoesNotContain("Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation", assets);
        var dll = Path.Combine(consumer, "bin", "Release", tfm, "PackageConsumer.dll");
        async Task Build() => await runner.Run("build", project, "-c", "Release", "--no-restore");
        async Task Render(string marker) => await runner.Run(dll, marker);
        await Build();
        await Render("first");
        // The same virtual-only consumer requires no manual Razor properties under
        // any supported SDK. Restore after changing SDK to refresh its assets.
        var plainProject = await File.ReadAllTextAsync(project);
        foreach (var sdk in new[] { "Microsoft.NET.Sdk.Razor", "Microsoft.NET.Sdk.Web" })
        {
            await File.WriteAllTextAsync(project, plainProject.Replace("Sdk=\"Microsoft.NET.Sdk\"", $"Sdk=\"{sdk}\"", StringComparison.Ordinal));
            await runner.Run("restore", project, "--configfile", config);
            await Build();
            await Render("first");
        }
        await File.WriteAllTextAsync(project, plainProject);
        await runner.Run("restore", project, "--configfile", config);
        await Build();
        // Deliberately no clean/rebuild: linked source edits must invalidate Razor.
        await File.WriteAllTextAsync(template, "@model string\n<p data-version=\"second\">@Model</p>");
        await Build();
        await Render("second");
        // A case-only C# rename must replace companion/Razor outputs on both
        // case-sensitive and case-insensitive filesystems.
        await File.WriteAllTextAsync(view, linkedView.Replace("class Linked", "class linked", StringComparison.Ordinal));
        await Build();
        await Render("second");
        await File.WriteAllTextAsync(view, linkedView);
        await Build();
        await Render("second");
        // Ambiguous filenames must fail consistently, including on case-sensitive
        // filesystems. These distinct physical files have the same logical identity.
        var duplicateDirectory = Path.Combine(root, "duplicates");
        Directory.CreateDirectory(duplicateDirectory);
        var duplicate = Path.Combine(duplicateDirectory, "Linked.virtual.cshtml");
        await File.WriteAllTextAsync(duplicate, "<p>Ambiguous template</p>");
        await File.WriteAllTextAsync(project, plainProject.Replace("</ItemGroup>",
            "<AdditionalFiles Include=\"../duplicates/Linked.virtual.cshtml\" /></ItemGroup>", StringComparison.Ordinal));
        await runner.Fail("Multiple .virtual.cshtml files map to", "build", project, "-c", "Release", "--no-restore");
        File.Delete(duplicate);
        await File.WriteAllTextAsync(project, plainProject);
        await File.WriteAllTextAsync(template, "@model MissingType\n<p>@Model</p>");
        await runner.Fail("(?s)(CS0246.*MissingType|MissingType.*CS0246)", "build", project, "-c", "Release", "--no-restore");
        await File.WriteAllTextAsync(template, "@model string\n<p data-version=\"recovered\">@Model</p>");
        await Build();
        await Render("recovered");

        // Delete only this test's literal fixture; never recursively remove a
        // caller-supplied directory. The unique root and logs remain for diagnosis.
        File.Delete(template);
        await File.WriteAllTextAsync(view, "// Linked view intentionally removed.");
        await Build();
        await Render("absent");
        Assert.Empty(Directory.GetFiles(Path.Combine(consumer, "obj", "Release", tfm, "VirtualViews"), "*.cshtml"));
        await File.WriteAllTextAsync(view, """
            using IL.VirtualViews.Attributes;
            using IL.VirtualViews.Interfaces;
            namespace PackageConsumer;
            [VirtualViewPath("/views/package.cshtml")]
            public class DynamicView : IVirtualView
            {
                public static string ViewContent => System.DateTime.UtcNow.ToString();
            }
            """);
        await runner.Fail("(?is)error (IVV\\w*|VVIEW\\w*).*Dynamic ViewContent.*not supported",
            "build", project, "-c", "Release", "--no-restore");
    }

    private sealed class DotNet(string root)
    {
        private int sequence;
        public Task Run(params string[] arguments) => Execute(arguments, null);
        public Task Fail(string pattern, params string[] arguments) => Execute(arguments, pattern);

        private async Task Execute(string[] arguments, string? failurePattern)
        {
            var info = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root, RedirectStandardOutput = true,
                RedirectStandardError = true, UseShellExecute = false
            };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            info.Environment["NUGET_PACKAGES"] = Path.Combine(root, "nuget-packages");
            info.Environment["NUGET_HTTP_CACHE_PATH"] = Path.Combine(root, "nuget-http-cache");
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start dotnet.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException($"dotnet {string.Join(' ', arguments)} exceeded eight minutes. Artifacts: {root}");
            }
            var output = await stdout + Environment.NewLine + await stderr;
            var log = Path.Combine(root, $"{++sequence:D2}-{Path.GetFileNameWithoutExtension(arguments[0])}.log");
            await File.WriteAllTextAsync(log, output);
            var context = $"dotnet {string.Join(' ', arguments)} exited {process.ExitCode}. Log: {log}\n{output}";
            if (failurePattern is null) Assert.True(process.ExitCode == 0, context);
            else
            {
                Assert.True(process.ExitCode != 0, "Expected failure: " + context);
                Assert.True(Regex.IsMatch(output, failurePattern), "Expected diagnostic " + failurePattern + ": " + context);
            }
        }
    }

    private const string RuntimeProgram = """
        using System.Diagnostics;
        using System.Reflection;
        using IL.VirtualViews.Extensions;
        using Microsoft.AspNetCore.Hosting;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Mvc;
        using Microsoft.AspNetCore.Mvc.Abstractions;
        using Microsoft.AspNetCore.Mvc.ModelBinding;
        using Microsoft.AspNetCore.Mvc.Razor;
        using Microsoft.AspNetCore.Mvc.Razor.Compilation;
        using Microsoft.AspNetCore.Mvc.Rendering;
        using Microsoft.AspNetCore.Mvc.ViewFeatures;
        using Microsoft.AspNetCore.Razor.Hosting;
        using Microsoft.AspNetCore.Routing;
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.FileProviders;
        var present = args[0] != "absent";
        var assembly = Assembly.GetExecutingAssembly();
        var items = new RazorCompiledItemLoader().LoadItems(assembly).ToArray();
        if (items.Length != (present ? 1 : 0)) throw new Exception("Wrong emitted compiled item count.");
        if (assembly.GetReferencedAssemblies().Any(a => a.Name!.Contains("RuntimeCompilation")))
            throw new Exception("Runtime compilation assembly referenced.");
        var services = new ServiceCollection();
        services.AddSingleton<IWebHostEnvironment>(new EmptyEnvironment());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(new DiagnosticListener("PackageConsumer"));
        services.AddSingleton<DiagnosticSource>(p => p.GetRequiredService<DiagnosticListener>());
        services.AddLogging();
        services.AddVirtualViewsCapabilities("PackageConsumer");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider;
        var compiler = scoped.GetRequiredService<IViewCompilerProvider>().GetCompiler();
        if (compiler.GetType().Assembly.GetName().Name!.Contains("RuntimeCompilation"))
            throw new Exception("Runtime compiler registered.");
        const string path = "/views/package.cshtml";
        var compiled = await compiler.CompileAsync(path);
        if ((compiled.Item is not null) != present) throw new Exception("Stale or missing virtual item.");
        if (!present) return;
        var http = new DefaultHttpContext { RequestServices = scoped };
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var result = scoped.GetRequiredService<IRazorViewEngine>().GetView(null, path, true);
        if (!result.Success) throw new Exception("Virtual view not registered.");
        using var writer = new StringWriter();
        var data = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = "Package <model>" };
        var context = new ViewContext(action, result.View, data,
            new TempDataDictionary(http, scoped.GetRequiredService<ITempDataProvider>()), writer, new HtmlHelperOptions());
        await result.View.RenderAsync(context);
        var html = writer.ToString();
        if (!html.Contains("data-version=\"" + args[0] + "\"") || !html.Contains("Package &lt;model&gt;"))
            throw new Exception("Unexpected rendered HTML: " + html);
        Console.WriteLine("PASS: compiled virtual item rendered with NullFileProvider: " + html);
        sealed class EmptyEnvironment : IWebHostEnvironment
        {
            public string EnvironmentName { get; set; } = "Production";
            public string ApplicationName { get; set; } = "PackageConsumer";
            public string WebRootPath { get; set; } = AppContext.BaseDirectory;
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
            public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
        """;
}
