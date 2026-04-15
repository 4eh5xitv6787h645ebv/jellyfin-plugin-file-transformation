using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FileTransformation.Infrastructure;

/// <summary>
/// Tracks a monotonic version counter that increments whenever plugin
/// configurations change on disk. The frontend polls this to know when
/// a soft reload is needed.
/// </summary>
public sealed class ConfigVersionService : IDisposable
{
    private readonly ILogger<ConfigVersionService> _logger;
    private readonly FileSystemWatcher? _watcher;
    private long _version;

    public ConfigVersionService(ILogger<ConfigVersionService> logger, IApplicationPaths appPaths)
    {
        _logger = logger;
        // Randomize initial version so it doesn't leak server start time
        _version = Random.Shared.NextInt64(1_000_000, 9_999_999);

        string? configPath = appPaths.PluginConfigurationsPath;
        if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
        {
            _logger.LogWarning("[FileTransformation] Config path '{Path}' not found, auto-refresh disabled", configPath ?? "(null)");
            return;
        }

        _watcher = new FileSystemWatcher(configPath, "*.xml")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
            IncludeSubdirectories = false,
        };

        _watcher.Changed += OnConfigChanged;
        _watcher.Created += OnConfigChanged;
        _watcher.Error += OnWatcherError;

        _logger.LogInformation("[FileTransformation] Config version watcher active on: {Path}", configPath);
    }

    public long Version => Interlocked.Read(ref _version);

    private void OnConfigChanged(object sender, FileSystemEventArgs e)
    {
        long newVersion = Interlocked.Increment(ref _version);
        _logger.LogInformation("[FileTransformation] Config change detected ({File}), version now {Version}", e.Name, newVersion);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "[FileTransformation] FileSystemWatcher error — config change detection may be unreliable");
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
