using System.Reflection;
using IL.Misc.Helpers;
using IL.VirtualViews.Attributes;
using IL.VirtualViews.ContentProvider;
using IL.VirtualViews.Interfaces;
using Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IL.VirtualViews.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVirtualViewsCapabilities(this IServiceCollection serviceCollection)
    {
        return serviceCollection.AddVirtualViewsCapabilities("*");
    }

    public static IServiceCollection AddVirtualViewsCapabilities(this IServiceCollection serviceCollection, params string[] assembliesFilter)
    {
        return serviceCollection.AddVirtualViewsCapabilities(_ => { }, assembliesFilter);
    }

    public static IServiceCollection AddVirtualViewsCapabilities(
        this IServiceCollection serviceCollection,
        Action<VirtualViewsRegistrationOptions> configureOptions,
        params string[] assembliesFilter)
    {
        serviceCollection
            .AddControllersWithViews()
            .AddRazorRuntimeCompilation();

        var options = new VirtualViewsRegistrationOptions();
        configureOptions(options);
        var debugPhysicalFilesEnabled = options.EnableDebugPhysicalFiles;
        var allAssemblies = TypesAndAssembliesHelper
            .GetAssemblies(assembliesFilter)
            .Where(assembly => !assembly.IsDynamic)
            .ToList();

        var supportedTypes = allAssemblies
            .SelectMany(GetSupportedTypesForAssembly)
            .Distinct()
            .ToList();

        serviceCollection.AddOptions<MvcRazorRuntimeCompilationOptions>().Configure<IConfiguration>((runtimeCompilationOptions, configuration) =>
        {
            var isDebugEnabled = debugPhysicalFilesEnabled ?? configuration.GetValue<bool>("VirtualViews:Debug");
            RemoveVirtualViewsProviders(runtimeCompilationOptions);
            runtimeCompilationOptions.FileProviders.Add(new VirtualViewsProvider(supportedTypes, isDebugEnabled));
        });

        return serviceCollection;
    }

    private static IEnumerable<Type> GetSupportedTypesForAssembly(Assembly assembly)
    {
        var reflectionTypes = TypesAndAssembliesHelper
            .GetExportedTypes(assembly)
            .Where(type => type is { IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(HasVirtualViewsAttributeSafely)
            .Where(HasIVirtualViewInterface);

        return reflectionTypes;
    }

    private static bool HasVirtualViewsAttributeSafely(Type type)
    {
        try
        {
            return type
                .CustomAttributes
                .Any(x => typeof(VirtualViewPathAttribute).IsAssignableFrom(x.AttributeType));
        }
        catch
        {
            return false;
        }
    }

    private static bool HasIVirtualViewInterface(Type type)
    {
        return type.GetInterfaces().Any(x => x == typeof(IVirtualView));
    }

    private static void RemoveVirtualViewsProviders(MvcRazorRuntimeCompilationOptions runtimeCompilationOptions)
    {
        for (var i = runtimeCompilationOptions.FileProviders.Count - 1; i >= 0; i--)
        {
            if (runtimeCompilationOptions.FileProviders[i] is VirtualViewsProvider)
            {
                runtimeCompilationOptions.FileProviders.RemoveAt(i);
            }
        }
    }
}
