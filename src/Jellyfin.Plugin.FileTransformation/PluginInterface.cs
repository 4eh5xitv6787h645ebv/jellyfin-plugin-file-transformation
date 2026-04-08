using Jellyfin.Plugin.FileTransformation.Helpers;
using Jellyfin.Plugin.FileTransformation.Library;
using Jellyfin.Plugin.FileTransformation.Models;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.FileTransformation
{
    public static class PluginInterface
    {
        public static void RegisterTransformation(JObject payload)
        {
            IWebFileTransformationWriteService writeService = FileTransformationPlugin.Instance.ServiceProvider
                .GetRequiredService<IWebFileTransformationWriteService>();

            TransformationRegistrationPayload? castedPayload = payload.ToObject<TransformationRegistrationPayload>();

            if (castedPayload != null)
            {
                writeService.AddTransformation(castedPayload.Id, castedPayload.FileNamePattern, async (path, contents) =>
                {
                    ILogger logger = FileTransformationPlugin.Instance.ServiceProvider.GetRequiredService<IFileTransformationLogger>();
                    IServerApplicationHost serverApplicationHost = FileTransformationPlugin.Instance.ServiceProvider.GetRequiredService<IServerApplicationHost>();

                    await TransformationHelper.ApplyTransformation(path, contents, castedPayload, logger, serverApplicationHost);
                });
            }
        }

        /// <summary>
        /// Registers a controller/action pair to receive Cache-Control: no-store
        /// response headers via the global no-cache action filter. Other plugins
        /// invoke this via reflection (the same pattern as <see cref="RegisterTransformation"/>)
        /// because Jellyfin loads plugins into separate AssemblyLoadContexts and
        /// direct type references would not resolve.
        ///
        /// Example payload:
        ///   { "controllerName": "MyPlugin", "actionName": "GetConfig" }
        /// </summary>
        public static void RegisterNoCacheEndpoint(JObject payload)
        {
            NoCacheEndpointRegistrationPayload? castedPayload = payload.ToObject<NoCacheEndpointRegistrationPayload>();

            if (castedPayload == null
                || string.IsNullOrWhiteSpace(castedPayload.ControllerName)
                || string.IsNullOrWhiteSpace(castedPayload.ActionName))
            {
                return;
            }

            INoCacheEndpointRegistry registry = FileTransformationPlugin.Instance.ServiceProvider
                .GetRequiredService<INoCacheEndpointRegistry>();
            registry.Register(castedPayload.ControllerName, castedPayload.ActionName);
        }
    }
}