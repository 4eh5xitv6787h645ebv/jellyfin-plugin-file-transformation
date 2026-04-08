using Jellyfin.Plugin.FileTransformation.Library;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.FileTransformation.Infrastructure
{
    /// <summary>
    /// Global MVC action filter that adds Cache-Control: no-store headers to
    /// the configured set of plugin configuration endpoints.
    ///
    /// Matches on <see cref="ActionExecutingContext.ActionDescriptor"/> RouteValues
    /// after routing has resolved the request, so the filter is unaffected by
    /// Jellyfin's BaseUrl prefix (which trips up middleware-based approaches).
    ///
    /// The set of (controller, action) pairs to target is owned by
    /// <see cref="INoCacheEndpointRegistry"/> and can be extended at startup by
    /// other plugins via <see cref="PluginInterface.RegisterNoCacheEndpoint"/>.
    /// </summary>
    public sealed class NoCacheConfigFilter : IActionFilter
    {
        private readonly INoCacheEndpointRegistry m_registry;

        public NoCacheConfigFilter(INoCacheEndpointRegistry registry)
        {
            m_registry = registry;
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            var routeValues = context.ActionDescriptor.RouteValues;

            if (routeValues.TryGetValue("controller", out var controller)
                && routeValues.TryGetValue("action", out var action)
                && controller is not null
                && action is not null
                && m_registry.Contains(controller, action))
            {
                var headers = context.HttpContext.Response.Headers;
                headers[HeaderNames.CacheControl] = "no-store, no-cache, max-age=0, must-revalidate";
                headers[HeaderNames.Pragma] = "no-cache";
                headers[HeaderNames.Expires] = "0";
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            // No post-execution work needed.
        }
    }
}
