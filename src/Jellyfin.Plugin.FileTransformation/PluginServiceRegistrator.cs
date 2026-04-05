using System.Reflection;
using Jellyfin.Plugin.FileTransformation.Library;
using Jellyfin.Plugin.FileTransformation.Infrastructure;
using Jellyfin.Plugin.FileTransformation.Helpers;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FileTransformation
{
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        // Pre-created singleton instances that survive regardless of DI container timing.
        // The Harmony prefix on Startup.Configure() may fire before the DI container is
        // fully built, causing GetService<T>() to return null even though RegisterServices
        // already ran. By creating the instances eagerly here we eliminate the race.
        private static WebFileTransformationService? s_transformationService;
        private static IFileTransformationLogger? s_transformationLogger;

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

            // Create instances eagerly so they are available to the Harmony prefix
            // on Startup.Configure() even if the DI container hasn't been built yet.
            if (loggerFactory != null)
            {
                s_transformationLogger ??= new FileTransformationLogger(loggerFactory.CreateLogger<FileTransformationPlugin>());
                s_transformationService ??= new WebFileTransformationService(s_transformationLogger);
            }

            if (s_transformationService != null && s_transformationLogger != null)
            {
                // Register the pre-created instances so DI consumers get the same singletons.
                serviceCollection.AddSingleton<WebFileTransformationService>(s_transformationService);
                serviceCollection.AddSingleton<IWebFileTransformationReadService>(s_transformationService);
                serviceCollection.AddSingleton<IWebFileTransformationWriteService>(s_transformationService);
                serviceCollection.AddSingleton<IFileTransformationLogger>(s_transformationLogger);
            }
            else
            {
                // Fallback: let DI create the instances (original behavior).
                serviceCollection.AddSingleton<WebFileTransformationService>()
                    .AddSingleton<IWebFileTransformationReadService>(s => s.GetRequiredService<WebFileTransformationService>())
                    .AddSingleton<IWebFileTransformationWriteService>(s => s.GetRequiredService<WebFileTransformationService>());
                serviceCollection.AddSingleton<IFileTransformationLogger, FileTransformationLogger>();
            }

            logger?.LogInformation("[FileTransformation] DI services registered successfully.");
        }

        private IFileProvider GetFileTransformationFileProvider(IServerConfigurationManager serverConfigurationManager, IApplicationBuilder mainApplicationBuilder)
        {
            // Prefer the eagerly-created instances (always available), fall back to DI.
            IWebFileTransformationReadService? readService = s_transformationService
                ?? mainApplicationBuilder.ApplicationServices.GetService<IWebFileTransformationReadService>();
            IFileTransformationLogger? transformationLogger = s_transformationLogger
                ?? mainApplicationBuilder.ApplicationServices.GetService<IFileTransformationLogger>();

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
