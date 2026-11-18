using System.Reflection;
using IL.Misc.Helpers;
using IL.VirtualViews.Attributes;
using IL.VirtualViews.Interfaces;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;

namespace IL.VirtualViews.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVirtualViewsCapabilities(this IServiceCollection serviceCollection)
        => serviceCollection.AddVirtualViewsCapabilities("*");

    public static IServiceCollection AddVirtualViewsCapabilities(this IServiceCollection serviceCollection, params string[] assembliesFilter)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);
        var assemblies = TypesAndAssembliesHelper.GetAssemblies(assembliesFilter)
            .Where(assembly => !assembly.IsDynamic).Distinct().ToList();
        var supportedTypes = assemblies.SelectMany(GetSupportedTypesForAssembly).Distinct().ToList();
        var mvc = serviceCollection.AddControllersWithViews();
        mvc.ConfigureApplicationPartManager(manager =>
        {
            foreach (var assembly in supportedTypes.Select(type => type.Assembly).Distinct())
            {
                // A plain Razor SDK library need not advertise a Razor application-part
                // factory. Add its compiled items explicitly, without discovering unrelated
                // controllers from every assembly matched by the registration filter.
                if (!manager.ApplicationParts.OfType<CompiledRazorAssemblyPart>().Any(part => part.Assembly == assembly))
                    manager.ApplicationParts.Add(new CompiledRazorAssemblyPart(assembly));
            }

            var provider = manager.FeatureProviders.OfType<VirtualViewsFeatureProvider>().SingleOrDefault();
            if (provider == null)
            {
                provider = new VirtualViewsFeatureProvider();
                manager.FeatureProviders.Add(provider);
            }
            provider.AddTypes(supportedTypes);
        });
        return serviceCollection;
    }

    private static IEnumerable<Type> GetSupportedTypesForAssembly(Assembly assembly)
        => TypesAndAssembliesHelper.GetExportedTypes(assembly)
            .Where(type => type is { IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(type => typeof(IVirtualView).IsAssignableFrom(type))
            .Where(HasVirtualViewsAttributeSafely);

    private static bool HasVirtualViewsAttributeSafely(Type type)
    {
        try
        {
            return type.GetCustomAttributes<VirtualViewPathAttribute>(inherit: true).Any();
        }
        catch
        {
            return false;
        }
    }
}
