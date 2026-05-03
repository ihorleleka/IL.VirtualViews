using Microsoft.AspNetCore.Builder;

namespace IL.VirtualViews.Extensions;

public static class WebAppBuilderExtensions
{
    public static WebApplicationBuilder AddVirtualViewsCapabilities(this WebApplicationBuilder builder)
    {
        builder.Services.AddVirtualViewsCapabilities("*");
        return builder;
    }

    public static WebApplicationBuilder AddVirtualViewsCapabilities(this WebApplicationBuilder builder, params string[] assembliesFilter)
    {
        builder.Services.AddVirtualViewsCapabilities(assembliesFilter);
        return builder;
    }

    public static WebApplicationBuilder AddVirtualViewsCapabilities(
        this WebApplicationBuilder builder,
        Action<VirtualViewsRegistrationOptions> configureOptions,
        params string[] assembliesFilter)
    {
        builder.Services.AddVirtualViewsCapabilities(configureOptions, assembliesFilter);
        return builder;
    }
}
