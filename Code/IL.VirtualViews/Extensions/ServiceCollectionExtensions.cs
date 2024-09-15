﻿using System.Reflection;
using IL.Misc.Helpers;
using IL.VirtualViews.Attributes;
using IL.VirtualViews.ContentProvider;
using IL.VirtualViews.Interfaces;
using Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation;
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
        serviceCollection
            .AddControllersWithViews()
            .AddRazorRuntimeCompilation();


        var supportedTypes = TypesAndAssembliesHelper
            .GetAssemblies(assembliesFilter)
            .Where(assembly => !assembly.IsDynamic)
            .SelectMany(TypesAndAssembliesHelper.GetExportedTypes)
            .Where(type => type is { IsAbstract: false, IsGenericTypeDefinition: false } && type.CustomAttributes.Any())
            .Where(HasVirtualViewsAttributeOrOneOfDerivatives)
            .Where(HasIVirtualViewInterface)
            .ToList();

        serviceCollection.Configure<MvcRazorRuntimeCompilationOptions>(options => { options.FileProviders.Add(new VirtualViewsProvider(supportedTypes)); });

        return serviceCollection;
    }


    private static bool HasVirtualViewsAttributeOrOneOfDerivatives(Type type)
    {
        return type
            .GetCustomAttributes()
            .Any(x => x is VirtualViewPathAttribute);
    }

    private static bool HasIVirtualViewInterface(Type type)
    {
        return type.GetInterfaces().Any(x => x == typeof(IVirtualView));
    }
}