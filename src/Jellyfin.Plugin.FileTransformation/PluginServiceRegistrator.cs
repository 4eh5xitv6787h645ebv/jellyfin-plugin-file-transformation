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
        private static FileTransformationLogger? s_transformationLogger;

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

            // Try to create instances eagerly so they are available to the Harmony prefix
            // on Startup.Configure() even if the DI container hasn't been built yet.
            // Old statics are kept until replacements are ready so concurrent Harmony
            // prefix calls never observe a null window during in-process restarts.
            if (loggerFactory != null)
            {
                try
                {
                    var tempLogger = new FileTransformationLogger(loggerFactory.CreateLogger<FileTransformationPlugin>());
                    var tempService = new WebFileTransformationService(tempLogger);
                    s_transformationLogger = tempLogger;
                    s_transformationService = tempService;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    logger?.LogError(ex, "[FileTransformation] Eager instance creation failed. " +
                        "DI-managed creation will be attempted as fallback but may also fail for the same reason.");
                    s_transformationLogger = null;
                    s_transformationService = null;
                }
            }
            else
            {
                // loggerFactory unavailable -- clear statics since we cannot create
                // fresh instances and must not carry stale ones into the new host.
                s_transformationService = null;
                s_transformationLogger = null;
            }

            // DI registrations always happen via one of two paths: pre-created
            // instances when available, or DI-managed factory creation as fallback.
            if (s_transformationService != null && s_transformationLogger != null)
            {
                serviceCollection.AddSingleton<WebFileTransformationService>(s_transformationService);
                serviceCollection.AddSingleton<IWebFileTransformationReadService>(s_transformationService);
                serviceCollection.AddSingleton<IWebFileTransformationWriteService>(s_transformationService);
                serviceCollection.AddSingleton<IFileTransformationLogger>(s_transformationLogger);
            }
            else
            {
                serviceCollection.AddSingleton<WebFileTransformationService>()
                    .AddSingleton<IWebFileTransformationReadService>(s => s.GetRequiredService<WebFileTransformationService>())
                    .AddSingleton<IWebFileTransformationWriteService>(s => s.GetRequiredService<WebFileTransformationService>());
                serviceCollection.AddSingleton<IFileTransformationLogger, FileTransformationLogger>();
            }

            logger?.LogInformation("[FileTransformation] DI services registered successfully.");
        }

        private IFileProvider GetFileTransformationFileProvider(IServerConfigurationManager serverConfigurationManager, IApplicationBuilder mainApplicationBuilder)
        {
            // Prefer the eagerly-created instances (always available regardless of DI
            // container timing), fall back to DI resolution as a last resort.
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
