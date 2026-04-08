using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.FileTransformation.Models
{
    /// <summary>
    /// Payload for registering an endpoint with the no-cache list.
    /// Used by both the REST API (FileTransformationController) and the
    /// reflection-based PluginInterface entry point.
    /// </summary>
    public class NoCacheEndpointRegistrationPayload
    {
        /// <summary>
        /// MVC controller name without the "Controller" suffix (e.g. "Dashboard").
        /// </summary>
        [JsonPropertyName("controllerName")]
        public string ControllerName { get; set; } = string.Empty;

        /// <summary>
        /// MVC action method name (e.g. "GetConfigurationPages").
        /// </summary>
        [JsonPropertyName("actionName")]
        public string ActionName { get; set; } = string.Empty;
    }
}
