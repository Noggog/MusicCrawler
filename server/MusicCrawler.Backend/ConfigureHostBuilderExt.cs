using Autofac;
using Autofac.Extensions.DependencyInjection;

namespace MusicCrawler.Backend;

public static class ConfigureHostBuilderExt
{
    public static void RegisterAutofacModule<TModule>(this ConfigureHostBuilder host)
        where TModule : Module, new()
    {
        host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

        host.ConfigureContainer<ContainerBuilder>(
            builder => { builder.RegisterModule<TModule>(); });
    }

    public static void RegisterAutofacModule<TModule>(this ConfigureHostBuilder host, TModule module)
        where TModule : Module
    {
        host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

        host.ConfigureContainer<ContainerBuilder>(
            builder => { builder.RegisterModule(module); });
    }
}