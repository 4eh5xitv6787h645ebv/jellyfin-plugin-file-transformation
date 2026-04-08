using System.Reflection;
using Jellyfin.Plugin.FileTransformation.Library;
using Jellyfin.Plugin.FileTransformation.Infrastructure;
using Jellyfin.Plugin.FileTransformation.Helpers;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FileTransformation
{
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            IServerApplicationPaths? applicationPaths = (IServerApplicationPaths?)applicationHost.GetType().GetProperty("ApplicationPaths", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(applicationHost);
            ILoggerFactory? loggerFactory = (ILoggerFactory?)applicationHost.GetType().GetProperty("LoggerFactory", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(applicationHost);
            ILogger? logger = loggerFactory?.CreateLogger(typeof(PluginServiceRegistrator).FullName ?? typeof(PluginServiceRegistrator).Name);

            logger?.LogInformation("[FileTransformation] RegisterServices called. Initializing module...");

            ModuleInitializer.Initialize(applicationPaths, loggerFactory?.CreateLogger(typeof(ModuleInitializer).FullName ?? typeof(ModuleInitializer).Name));

            logger?.LogInformation("[FileTransformation] ModuleInitializer complete. Setting up delegates...");

            StartupHelper.WebDefaultFilesFileProvider = GetFileTransformationFileProvider;
            StartupHelper.WebStaticFilesFileProvider = GetFileTransformationFileProvider;

            logger?.LogInformation("[FileTransformation] Delegates set. Registering DI services...");

            serviceCollection.AddSingleton<WebFileTransformationService>()
                .AddSingleton<IWebFileTransformationReadService>(s => s.GetRequiredService<WebFileTransformationService>())
                .AddSingleton<IWebFileTransformationWriteService>(s => s.GetRequiredService<WebFileTransformationService>());

            serviceCollection.AddSingleton<IFileTransformationLogger, FileTransformationLogger>();

            // No-cache config filter — applies cache-busting headers to plugin
            // configuration endpoints so admin config changes are picked up by
            // clients without a hard browser refresh. Benefits all plugins, not
            // just the ones that ship their own cache-busting logic.
            //
            // Registration order:
            //   1. Registry as singleton (built-in defaults populated in ctor)
            //   2. Filter as singleton (depends on registry)
            //   3. PostConfigure<MvcOptions> to add the filter to the global pipeline
            //
            // PostConfigure is required (not Configure) because Jellyfin's
            // AddJellyfinApi(...).AddMvc(...) runs after plugin RegisterServices
            // and would otherwise overwrite our filter list. PostConfigure runs
            // after all Configure callbacks, so our addition is preserved.
            serviceCollection.AddSingleton<INoCacheEndpointRegistry, NoCacheEndpointRegistry>();
            serviceCollection.AddSingleton<NoCacheConfigFilter>();
            serviceCollection.PostConfigure<MvcOptions>(options =>
            {
                options.Filters.AddService<NoCacheConfigFilter>();
            });

            logger?.LogInformation("[FileTransformation] DI services registered successfully.");
        }

        private IFileProvider GetFileTransformationFileProvider(IServerConfigurationManager serverConfigurationManager, IApplicationBuilder mainApplicationBuilder)
        {
            IWebFileTransformationReadService? readService = mainApplicationBuilder.ApplicationServices.GetService<IWebFileTransformationReadService>();
            IFileTransformationLogger? transformationLogger = mainApplicationBuilder.ApplicationServices.GetService<IFileTransformationLogger>();

            if (readService == null || transformationLogger == null)
            {
                // Services were not registered — likely the plugin was disabled during type validation
                // or RegisterServices was not called. Fall back to default file provider.
                ILogger? fallbackLogger = mainApplicationBuilder.ApplicationServices.GetService<ILoggerFactory>()
                    ?.CreateLogger(typeof(PluginServiceRegistrator).FullName ?? typeof(PluginServiceRegistrator).Name);
                fallbackLogger?.LogWarning(
                    "[FileTransformation] IWebFileTransformationReadService or IFileTransformationLogger not found in DI container. " +
                    "File transformations are DISABLED. Falling back to default PhysicalFileProvider.");
                return new PhysicalFileProvider(serverConfigurationManager.ApplicationPaths.WebPath);
            }

            return new PhysicalTransformedFileProvider(
                new PhysicalFileProvider(serverConfigurationManager.ApplicationPaths.WebPath),
                readService,
                transformationLogger);
        }
    }
}
