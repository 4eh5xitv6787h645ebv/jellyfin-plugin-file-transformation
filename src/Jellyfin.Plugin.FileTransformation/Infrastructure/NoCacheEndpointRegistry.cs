using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Jellyfin.Plugin.FileTransformation.Library;

namespace Jellyfin.Plugin.FileTransformation.Infrastructure
{
    /// <summary>
    /// Thread-safe singleton implementation of <see cref="INoCacheEndpointRegistry"/>.
    /// Pre-populated with Jellyfin core endpoints that serve plugin configuration data.
    /// </summary>
    public sealed class NoCacheEndpointRegistry : INoCacheEndpointRegistry
    {
        // ConcurrentDictionary used as a thread-safe set: plugins may call Register
        // from any thread during startup, the action filter reads on every request.
        private readonly ConcurrentDictionary<(string Controller, string Action), byte> m_endpoints
            = new(StringTupleComparer.Instance);

        public NoCacheEndpointRegistry()
        {
            // Built-in defaults: Jellyfin core endpoints that serve plugin configuration data.
            // Without no-store headers, browsers (Firefox in particular) cache these responses
            // across SPA navigations and admin config changes appear to not apply until the
            // user performs a hard refresh.
            //
            // Verified controller/action names against Jellyfin 10.11.x source:
            //   Jellyfin.Api/Controllers/DashboardController.cs
            //   Jellyfin.Api/Controllers/PluginsController.cs
            Register("Dashboard", "GetConfigurationPages");
            Register("Dashboard", "GetDashboardConfigurationPage");
            Register("Plugins", "GetPluginConfiguration");
        }

        public void Register(string controllerName, string actionName)
        {
            if (string.IsNullOrWhiteSpace(controllerName) || string.IsNullOrWhiteSpace(actionName))
            {
                return;
            }

            m_endpoints.TryAdd((controllerName, actionName), 0);
        }

        public bool Contains(string controllerName, string actionName)
            => m_endpoints.ContainsKey((controllerName, actionName));

        /// <summary>
        /// Case-insensitive equality comparer for (controller, action) tuples.
        /// </summary>
        private sealed class StringTupleComparer : IEqualityComparer<(string, string)>
        {
            public static readonly StringTupleComparer Instance = new();

            public bool Equals((string, string) x, (string, string) y)
                => string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode((string, string) obj)
                => HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1 ?? string.Empty),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2 ?? string.Empty));
        }
    }
}
