using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.FileTransformation.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FileTransformation.Infrastructure;

public class WebFileTransformationService : IWebFileTransformationReadService, IWebFileTransformationWriteService
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<string, ICollection<(Guid TransformId, TransformFile Delegate)>> _fileTransformations = new();
    private readonly ConcurrentDictionary<string, Regex> _regexCache = new();
    private readonly object _pipelineLock = new();
    private readonly ILogger<FileTransformationPlugin> _logger;

    public WebFileTransformationService(IFileTransformationLogger logger)
    {
        _logger = logger;
    }

    private static string NormalizePath(string path)
    {
        return path.TrimStart('/');
    }

    private Regex GetOrCreateRegex(string pattern)
    {
        return _regexCache.GetOrAdd(pattern, p => new Regex(p, RegexOptions.Compiled, RegexTimeout));
    }

    public bool NeedsTransformation(string path)
    {
        path = NormalizePath(path);

        if (_fileTransformations.ContainsKey(path))
        {
            return true;
        }

        return _fileTransformations.Keys.Any(key =>
        {
            try
            {
                return GetOrCreateRegex(key).IsMatch(path);
            }
            catch (RegexMatchTimeoutException)
            {
                _logger.LogWarning("[FileTransformation] Regex timeout matching pattern '{Pattern}' against '{Path}'", key, path);
                return false;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "[FileTransformation] Invalid regex pattern '{Pattern}'", key);
                return false;
            }
        });
    }

    public async Task RunTransformation(string path, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        path = NormalizePath(path);

        // Find the matching pipeline — exact match first, then regex
        ICollection<(Guid TransformId, TransformFile Delegate)>? pipeline = null;

        if (_fileTransformations.TryGetValue(path, out ICollection<(Guid TransformId, TransformFile Delegate)>? exactMatch))
        {
            pipeline = exactMatch;
        }
        else
        {
            string? key = _fileTransformations.Keys.FirstOrDefault(k =>
            {
                try
                {
                    return GetOrCreateRegex(k).IsMatch(path);
                }
                catch (Exception ex) when (ex is RegexMatchTimeoutException or ArgumentException)
                {
                    _logger.LogWarning(ex, "[FileTransformation] Regex error for pattern '{Pattern}'", k);
                    return false;
                }
            });

            if (key != null)
            {
                _fileTransformations.TryGetValue(key, out pipeline);
            }
        }

        if (pipeline == null)
        {
            return;
        }

        // Snapshot under lock to avoid races with Add/Remove
        List<(Guid TransformId, TransformFile Delegate)> transforms;
        lock (_pipelineLock)
        {
            transforms = pipeline.ToList();
        }

        foreach ((Guid transformId, TransformFile action) in transforms)
        {
            try
            {
                stream.Seek(0, SeekOrigin.Begin);
                await action(path, stream).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[FileTransformation] Transform {TransformId} failed for '{Path}'. Continuing with next.",
                    transformId, path);
            }
        }

        stream.Seek(0, SeekOrigin.Begin);
    }

    public void AddTransformation(Guid id, string path, TransformFile transformation)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(transformation);

        path = NormalizePath(path);
        _logger.LogInformation("[FileTransformation] Registering transformation for '{Path}' (ID: {Id})", path, id);

        lock (_pipelineLock)
        {
            if (!_fileTransformations.TryGetValue(path, out ICollection<(Guid TransformId, TransformFile Delegate)>? pipeline))
            {
                pipeline = new List<(Guid TransformId, TransformFile Delegate)>();
                _fileTransformations[path] = pipeline;
            }

            if (!pipeline.Any(x => x.TransformId == id))
            {
                pipeline.Add((id, transformation));
            }
        }
    }

    public void RemoveTransformation(Guid id)
    {
        lock (_pipelineLock)
        {
            List<string> emptyKeys = new List<string>();

            foreach (KeyValuePair<string, ICollection<(Guid TransformId, TransformFile Delegate)>> pipelines in _fileTransformations)
            {
                (Guid TransformId, TransformFile Delegate) match = pipelines.Value.FirstOrDefault(x => x.TransformId == id);
                if (match != default)
                {
                    pipelines.Value.Remove(match);
                    if (pipelines.Value.Count == 0)
                    {
                        emptyKeys.Add(pipelines.Key);
                    }
                }
            }

            // Clean up empty pipelines and stale regex cache entries
            foreach (string key in emptyKeys)
            {
                _fileTransformations.TryRemove(key, out _);
                _regexCache.TryRemove(key, out _);
            }
        }
    }

    public void UpdateTransformation(Guid id, string path, TransformFile transformation)
    {
        RemoveTransformation(id);
        AddTransformation(id, path, transformation);
    }
}
