using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FileTransformation.Infrastructure;

/// <summary>
/// IStartupFilter that injects the FileTransformationMiddleware before Jellyfin's
/// own Startup.Configure() pipeline. This replaces the Harmony-based approach of
/// patching Startup.Configure() and substituting the IFileProvider.
///
/// The middleware intercepts responses for /web/ paths and runs registered
/// transformations on the response body before sending it to the client.
/// </summary>
public sealed class FileTransformationStartupFilter : IStartupFilter
{
    private readonly ILogger<FileTransformationStartupFilter> _logger;

    public FileTransformationStartupFilter(ILogger<FileTransformationStartupFilter> logger)
    {
        _logger = logger;
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            _logger.LogInformation("[FileTransformation] Installing response transformation middleware");

            app.UseMiddleware<FileTransformationMiddleware>();

            next(app);
        };
    }
}
