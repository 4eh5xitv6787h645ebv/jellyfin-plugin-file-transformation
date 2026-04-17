using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FileTransformation.Infrastructure
{
    public sealed class ConfigVersionService : IDisposable
    {
        private readonly ILogger<ConfigVersionService> m_logger;
        private readonly FileSystemWatcher? m_watcher;
        private long m_version;

        public ConfigVersionService(ILogger<ConfigVersionService> logger, IApplicationPaths appPaths)
        {
            m_logger = logger;
            m_version = Random.Shared.NextInt64(1000000, 9999999);

            string? configPath = appPaths.PluginConfigurationsPath;
            if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
            {
                m_logger.LogWarning($"[FileTransformation] Config path '{configPath ?? "(null)"}' not found, auto-refresh disabled");
                return;
            }

            m_watcher = new FileSystemWatcher(configPath, "*.xml")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false,
            };

            m_watcher.Changed += OnConfigChanged;
            m_watcher.Created += OnConfigChanged;
            m_watcher.Error += OnWatcherError;

            m_logger.LogInformation($"[FileTransformation] Config version watcher active on: {configPath}");
        }

        public long Version => Interlocked.Read(ref m_version);

        private void OnConfigChanged(object sender, FileSystemEventArgs e)
        {
            long newVersion = Interlocked.Increment(ref m_version);
            m_logger.LogInformation($"[FileTransformation] Config change detected ({e.Name}), version now {newVersion}");
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            m_logger.LogError(e.GetException(), "[FileTransformation] FileSystemWatcher error — config change detection may be unreliable");
        }

        public void Dispose()
        {
            m_watcher?.Dispose();
        }
    }
}
