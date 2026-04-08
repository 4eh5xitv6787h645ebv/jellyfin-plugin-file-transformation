using System.Net;
using System.Reflection;
using System.Text;
using Jellyfin.Plugin.FileTransformation.Helpers;
using Jellyfin.Plugin.FileTransformation.Library;
using Jellyfin.Plugin.FileTransformation.Models;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.FileTransformation.Controller
{
    [Route("[controller]")]
    public class FileTransformationController : ControllerBase
    {
        private readonly IServerApplicationHost m_serverApplicationHost;
        private readonly ILogger<FileTransformationPlugin> m_logger;

        public FileTransformationController(IServerApplicationHost serverApplicationHost, IFileTransformationLogger logger)
        {
            m_serverApplicationHost = serverApplicationHost;
            m_logger = logger;
        }
        
        [HttpPost("RegisterTransformation")]
        [Authorize(Policy = Policies.RequiresElevation)]
        public ActionResult RegisterTransformation([FromBody] TransformationRegistrationPayload payload, [FromServices] IWebFileTransformationWriteService writeService)
        {
            writeService.AddTransformation(payload.Id, payload.FileNamePattern, async (path, contents) =>
            {
                await TransformationHelper.ApplyTransformation(path, contents, payload, m_logger, m_serverApplicationHost);
            });

            return Ok();
        }

        /// <summary>
        /// Adds a controller/action pair to the no-cache list so its MVC responses
        /// are returned with Cache-Control: no-store. Provided as a REST endpoint
        /// for symmetry with <see cref="RegisterTransformation"/>; plugins running
        /// in-process should prefer the reflection-based <see cref="PluginInterface.RegisterNoCacheEndpoint"/>.
        /// </summary>
        [HttpPost("RegisterNoCacheEndpoint")]
        [Authorize(Policy = Policies.RequiresElevation)]
        public ActionResult RegisterNoCacheEndpoint(
            [FromBody] NoCacheEndpointRegistrationPayload payload,
            [FromServices] INoCacheEndpointRegistry registry)
        {
            if (string.IsNullOrWhiteSpace(payload.ControllerName) || string.IsNullOrWhiteSpace(payload.ActionName))
            {
                return BadRequest("controllerName and actionName are required.");
            }

            registry.Register(payload.ControllerName, payload.ActionName);
            return Ok();
        }
    }
}