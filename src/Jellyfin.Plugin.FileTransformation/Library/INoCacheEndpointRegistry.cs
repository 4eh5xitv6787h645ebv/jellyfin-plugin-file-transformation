namespace Jellyfin.Plugin.FileTransformation.Library
{
    /// <summary>
    /// Registry of (controller, action) pairs whose MVC responses should be
    /// served with no-store cache headers. Consulted on every request by
    /// NoCacheConfigFilter.
    ///
    /// Pre-populated with Jellyfin core plugin configuration endpoints. Other
    /// plugins can register their own endpoints via
    /// <see cref="Jellyfin.Plugin.FileTransformation.PluginInterface.RegisterNoCacheEndpoint"/>
    /// or the FileTransformation REST API.
    /// </summary>
    public interface INoCacheEndpointRegistry
    {
        /// <summary>
        /// Adds an endpoint to the no-cache list. Idempotent and case-insensitive.
        /// </summary>
        /// <param name="controllerName">MVC controller name without the "Controller" suffix (e.g. "Dashboard").</param>
        /// <param name="actionName">MVC action method name (e.g. "GetConfigurationPages").</param>
        void Register(string controllerName, string actionName);

        /// <summary>
        /// Returns true if the given controller/action pair is registered.
        /// Comparison is case-insensitive.
        /// </summary>
        bool Contains(string controllerName, string actionName);
    }
}
