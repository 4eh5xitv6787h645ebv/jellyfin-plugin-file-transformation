using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.FileTransformation.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.FileTransformation.Infrastructure;

/// <summary>
/// ASP.NET Core middleware that intercepts responses for /web/ paths and runs
/// registered file transformations on the response body.
///
/// Handles both text (HTML, JS, CSS) and binary (images, icons) transformations
/// by working with raw Stream objects — the same TransformFile delegate signature
/// used by the original PhysicalTransformedFileProvider approach.
/// </summary>
public sealed class FileTransformationMiddleware
{
    private readonly RequestDelegate _next;

    private static string GetAutoRefreshScript(string mode, bool debug)
    {
        return """
        <script>
        (function(){
            var _ftVer=null,_ftMode='__FT_MODE__',_ftDebug=__FT_DEBUG__;
            var _ftShown=false,_ftPending=false,_ftActScheduled=false;
            var FT_MAX_RELOADS=3,FT_RELOAD_WINDOW=60000;
            function ftLog(){if(_ftDebug)console.log.apply(console,['[FT]'].concat(Array.prototype.slice.call(arguments)));}
            function ftDbg(){if(_ftDebug)console.debug.apply(console,['[FT]'].concat(Array.prototype.slice.call(arguments)));}
            function ftIsHome(){
                var h=window.location.hash||'';
                return h===''||h==='#/'||h==='#/home.html'||h.indexOf('#/home')===0;
            }
            function ftCanReload(){
                var now=Date.now();
                var s=sessionStorage;
                var count=parseInt(s.getItem('ft_rl_c')||'0',10);
                var start=parseInt(s.getItem('ft_rl_s')||'0',10);
                if(now-start>FT_RELOAD_WINDOW){
                    count=0;
                    start=now;
                }
                count++;
                s.setItem('ft_rl_c',count);
                s.setItem('ft_rl_s',start);
                if(count>FT_MAX_RELOADS){
                    ftLog('reload loop detected ('+count+' reloads in '+FT_RELOAD_WINDOW/1000+'s), stopping');
                    return false;
                }
                return true;
            }
            function ftAct(){
                _ftActScheduled=false;
                if(_ftMode==='reload'){
                    if(ftIsHome()){
                        if(ftCanReload()){
                            _ftPending=false;
                            ftLog('on home, reloading now');
                            window.location.reload();
                        }else{
                            _ftPending=false;
                            if(!_ftShown){
                                _ftShown=true;
                                ftShowToast();
                            }
                        }
                    }else if(!_ftShown){
                        _ftShown=true;
                        ftShowReloadToast();
                    }
                }else if(_ftMode==='toast'){
                    _ftPending=false;
                    if(!_ftShown){
                        _ftShown=true;
                        ftShowToast();
                    }
                }
            }
            function ftGetBase(){
                var p=window.location.pathname;
                var i=p.indexOf('/web');
                return i>0?p.substring(0,i):'';
            }
            function ftCheck(){
                var x=new XMLHttpRequest();
                x.open('GET',ftGetBase()+'/FileTransformation/config-version',true);
                x.onload=function(){
                    if(x.status===200){
                        try{
                            var v=JSON.parse(x.responseText).version;
                            if(_ftVer===null){_ftVer=v;ftDbg('init version:',v);return;}
                            if(v!==_ftVer){
                                ftLog('version changed:',_ftVer,'->',v);
                                _ftVer=v;
                                _ftPending=true;
                                _ftShown=false;
                            }
                            if(_ftPending&&!_ftActScheduled){
                                ftDbg('pending=true, isHome='+ftIsHome()+', hash='+window.location.hash);
                                _ftActScheduled=true;
                                ftDbg('acting in 3s (waiting for plugins to process config)');
                                setTimeout(ftAct,3000);
                            }
                        }catch(e){ftLog('poll error:',e);}
                    }
                };
                x.send();
            }
            function ftDismiss(el){el.remove();_ftShown=false;}
            function ftShowReloadToast(){
                if(document.getElementById('ft-config-toast'))return;
                var d=document.createElement('div');
                d.id='ft-config-toast';
                d.style.cssText='position:fixed;bottom:1.5em;left:50%;transform:translateX(-50%);z-index:10000;background:#1e1e1e;color:#eee;border:1px solid rgba(255,255,255,0.15);border-radius:8px;padding:0.8em 1.2em;display:flex;align-items:center;gap:1em;font-family:inherit;font-size:0.95em;box-shadow:0 4px 20px rgba(0,0,0,0.5);';
                var txt=document.createElement('span');
                txt.textContent='Settings changed. Will reload on home page.';
                d.appendChild(txt);
                var btn=document.createElement('button');
                btn.textContent='Reload Now';
                btn.style.cssText='background:#00a4dc;color:#fff;border:none;border-radius:4px;padding:0.4em 1em;cursor:pointer;font-size:0.9em;white-space:nowrap;';
                btn.onclick=function(){window.location.reload();};
                d.appendChild(btn);
                var close=document.createElement('button');
                close.textContent='\u00D7';
                close.style.cssText='background:none;border:none;color:#999;cursor:pointer;font-size:1.3em;padding:0 0.2em;line-height:1;';
                close.onclick=function(){ftDismiss(d);};
                d.appendChild(close);
                document.body.appendChild(d);
            }
            function ftShowToast(){
                if(document.getElementById('ft-config-toast'))return;
                var d=document.createElement('div');
                d.id='ft-config-toast';
                d.style.cssText='position:fixed;bottom:1.5em;left:50%;transform:translateX(-50%);z-index:10000;background:#1e1e1e;color:#eee;border:1px solid rgba(255,255,255,0.15);border-radius:8px;padding:0.8em 1.2em;display:flex;align-items:center;gap:1em;font-family:inherit;font-size:0.95em;box-shadow:0 4px 20px rgba(0,0,0,0.5);';
                var txt=document.createElement('span');
                txt.textContent='Plugin settings changed. Refresh to apply.';
                d.appendChild(txt);
                var btn=document.createElement('button');
                btn.textContent='Refresh';
                btn.style.cssText='background:#00a4dc;color:#fff;border:none;border-radius:4px;padding:0.4em 1em;cursor:pointer;font-size:0.9em;white-space:nowrap;';
                btn.onclick=function(){window.location.reload();};
                d.appendChild(btn);
                var close=document.createElement('button');
                close.textContent='\u00D7';
                close.style.cssText='background:none;border:none;color:#999;cursor:pointer;font-size:1.3em;padding:0 0.2em;line-height:1;';
                close.onclick=function(){ftDismiss(d);};
                d.appendChild(close);
                document.body.appendChild(d);
            }
            function ftStart(){
                setInterval(function(){
                    if(document.hidden)return;
                    ftCheck();
                },5000);
                window.addEventListener('hashchange',function(){setTimeout(ftCheck,500);});
                window.addEventListener('focus',function(){setTimeout(ftCheck,500);});
                document.addEventListener('visibilitychange',function(){if(!document.hidden)setTimeout(ftCheck,500);});
            }
            if(document.readyState==='loading'){
                document.addEventListener('DOMContentLoaded',ftStart);
            }else{
                ftStart();
            }
        })();
        </script>
        """.Replace("__FT_MODE__", mode).Replace("__FT_DEBUG__", debug ? "true" : "false");
    }

    public FileTransformationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IWebFileTransformationReadService readService,
        ILogger<FileTransformationMiddleware> logger)
    {
        string path = context.Request.Path.Value ?? string.Empty;

        // Fast path: only intercept /web/ paths.
        // Also handle /web (no trailing slash) which Jellyfin redirects.
        int webIndex = path.IndexOf("/web/", StringComparison.OrdinalIgnoreCase);
        if (webIndex < 0 && !path.EndsWith("/web", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Extract the relative path after /web/
        // When path is "/web/" or "/web", resolve to "index.html" since
        // Jellyfin's UseDefaultFiles serves index.html for the root.
        string relativePath = webIndex >= 0 ? path[(webIndex + 5)..] : string.Empty;
        if (string.IsNullOrEmpty(relativePath))
        {
            relativePath = "index.html";
        }

        // Always intercept index.html (for auto-refresh script injection).
        // For other paths, only intercept if a transformation is registered.
        bool isIndexHtml = string.Equals(relativePath, "index.html", StringComparison.OrdinalIgnoreCase);
        bool needsTransform = readService.NeedsTransformation(relativePath);

        if (!isIndexHtml && !needsTransform)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        logger.LogDebug("[FileTransformation] Intercepting response for: {Path}", relativePath);

        // Save the client's Accept-Encoding before stripping — we'll re-compress after transforming.
        string acceptEncoding = context.Request.Headers.AcceptEncoding.ToString();

        // Strip Accept-Encoding so Jellyfin serves uncompressed content into our buffer.
        context.Request.Headers.Remove("Accept-Encoding");

        // Buffer the response body so we can run transformations on it
        Stream originalBody = context.Response.Body;
        using MemoryStream bufferedBody = new MemoryStream();
        context.Response.Body = bufferedBody;

        try
        {
            await _next(context).ConfigureAwait(false);

            // Virtual file synthesis: if a transform is registered for a file that
            // doesn't exist on disk (404), create an empty response and let the
            // transform callbacks generate the content. This is how Plugin Pages
            // creates virtual pages like userpluginsettings.html.
            bool isSynthesized = false;
            if (context.Response.StatusCode == 404 && needsTransform)
            {
                logger.LogDebug("[FileTransformation] Synthesizing virtual file for '{Path}'", relativePath);
                isSynthesized = true;
                bufferedBody.SetLength(0);
                context.Response.StatusCode = 200;

                // Set content type from file extension using ASP.NET Core's built-in provider
                Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider contentTypeProvider = new();
                if (contentTypeProvider.TryGetContentType(relativePath, out string? contentType))
                {
                    context.Response.ContentType = contentType;
                }
                else
                {
                    context.Response.ContentType = "application/octet-stream";
                }
            }
            else if (context.Response.StatusCode != 200)
            {
                // Non-200 response with no transform registered — pass through
                bufferedBody.Seek(0, SeekOrigin.Begin);
                context.Response.Body = originalBody;
                await bufferedBody.CopyToAsync(context.Response.Body).ConfigureAwait(false);
                return;
            }

            // Run the transformation pipeline (only if transforms are registered)
            if (needsTransform)
            {
                try
                {
                    await readService.RunTransformation(relativePath, bufferedBody).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[FileTransformation] Transformation pipeline failed for '{Path}'. Serving original content.", relativePath);
                }

                // If this was a synthesized virtual file and the transforms produced nothing,
                // revert to 404 instead of serving an empty 200.
                if (isSynthesized && bufferedBody.Length == 0)
                {
                    logger.LogWarning("[FileTransformation] Virtual file synthesis for '{Path}' produced empty content, reverting to 404", relativePath);
                    context.Response.StatusCode = 404;
                    context.Response.ContentLength = 0;
                    context.Response.Body = originalBody;
                    return;
                }
            }

            // For index.html, inject the auto-refresh script after all transforms
            if (isIndexHtml)
            {
                try
                {
                    await InjectAutoRefreshScript(bufferedBody).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[FileTransformation] Failed to inject auto-refresh script");
                }
            }

            // Compute ETag from the transformed content so browsers can do conditional requests.
            // This brings back 304 responses for unchanged transformed content.
            bufferedBody.Seek(0, SeekOrigin.Begin);
            byte[] hashBytes = await SHA256.HashDataAsync(bufferedBody).ConfigureAwait(false);
            string etag = $"\"{Convert.ToHexStringLower(hashBytes[..8])}\"";

            // Check If-None-Match — return 304 if content hasn't changed
            string ifNoneMatch = context.Request.Headers.IfNoneMatch.ToString();
            if (!string.IsNullOrEmpty(ifNoneMatch) && string.Equals(ifNoneMatch, etag, StringComparison.Ordinal))
            {
                context.Response.StatusCode = 304;
                context.Response.ContentLength = 0;
                context.Response.Headers[HeaderNames.ETag] = etag;
                context.Response.Headers[HeaderNames.CacheControl] = "no-cache";
                context.Response.Body = originalBody;
                return;
            }

            // Prepare the response headers
            context.Response.Headers.Remove("Content-Encoding");
            context.Response.Headers.Remove("Last-Modified");
            context.Response.Headers[HeaderNames.ETag] = etag;
            context.Response.Headers[HeaderNames.CacheControl] = "no-cache";
            context.Response.Headers[HeaderNames.Vary] = "Accept-Encoding";

            // Re-compress the transformed content if the client supports it.
            // Skip compression for local/LAN clients where it just adds CPU overhead.
            bufferedBody.Seek(0, SeekOrigin.Begin);
            context.Response.Body = originalBody;

            // Parse Accept-Encoding tokens properly — honor q=0 (means "not supported")
            bool isLocal = IsLocalRequest(context);
            HashSet<string> encodings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string entry in acceptEncoding.Split(','))
            {
                string trimmed = entry.Trim();
                string[] parts = trimmed.Split(';');
                string token = parts[0].Trim();
                // Check for q=0 (exactly zero = "not accepted"). q=0.5, q=0.01 etc. are valid.
                bool rejected = false;
                for (int i = 1; i < parts.Length; i++)
                {
                    string param = parts[i].Trim();
                    if (param.StartsWith("q=", StringComparison.OrdinalIgnoreCase))
                    {
                        string qVal = param.Substring(2).Trim();
                        if (qVal == "0" || qVal == "0." || qVal == "0.0" || qVal == "0.00" || qVal == "0.000")
                        {
                            rejected = true;
                        }

                        break;
                    }
                }

                if (!rejected && token.Length > 0)
                {
                    encodings.Add(token);
                }
            }

            if (!isLocal && encodings.Contains("br"))
            {
                using MemoryStream compressed = new MemoryStream();
                using (BrotliStream brotli = new BrotliStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                {
                    await bufferedBody.CopyToAsync(brotli).ConfigureAwait(false);
                }

                context.Response.Headers[HeaderNames.ContentEncoding] = "br";
                context.Response.ContentLength = compressed.Length;
                compressed.Seek(0, SeekOrigin.Begin);
                await compressed.CopyToAsync(originalBody).ConfigureAwait(false);
            }
            else if (!isLocal && encodings.Contains("gzip"))
            {
                using MemoryStream compressed = new MemoryStream();
                using (GZipStream gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                {
                    await bufferedBody.CopyToAsync(gzip).ConfigureAwait(false);
                }

                context.Response.Headers[HeaderNames.ContentEncoding] = "gzip";
                context.Response.ContentLength = compressed.Length;
                compressed.Seek(0, SeekOrigin.Begin);
                await compressed.CopyToAsync(originalBody).ConfigureAwait(false);
            }
            else
            {
                context.Response.ContentLength = bufferedBody.Length;
                await bufferedBody.CopyToAsync(originalBody).ConfigureAwait(false);
            }
        }
        catch
        {
            context.Response.Body = originalBody;
            throw;
        }
    }

    private static bool IsLocalRequest(HttpContext context)
    {
        System.Net.IPAddress? remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp == null)
        {
            return true;
        }

        // Handle IPv6-mapped IPv4 (e.g. ::ffff:192.168.1.5 from Docker/dual-stack)
        if (remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        // Loopback (127.0.0.1, ::1)
        if (System.Net.IPAddress.IsLoopback(remoteIp))
        {
            return true;
        }

        // Same machine (remote == local)
        System.Net.IPAddress? localIp = context.Connection.LocalIpAddress;
        if (localIp != null)
        {
            if (localIp.IsIPv4MappedToIPv6)
            {
                localIp = localIp.MapToIPv4();
            }

            if (remoteIp.Equals(localIp))
            {
                return true;
            }
        }

        // Private/LAN ranges: 10.x, 172.16-31.x, 192.168.x
        byte[] bytes = remoteIp.GetAddressBytes();
        if (bytes.Length == 4)
        {
            if (bytes[0] == 10)
            {
                return true;
            }

            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task InjectAutoRefreshScript(MemoryStream body)
    {
        FileTransformationPlugin? plugin = FileTransformationPlugin.Instance;
        Configuration.ConfigChangeNotification notification = plugin?.Configuration?.ConfigChangeNotification
                           ?? Configuration.ConfigChangeNotification.Toast;

        if (notification == Configuration.ConfigChangeNotification.Disabled)
        {
            return;
        }

        string mode = notification == Configuration.ConfigChangeNotification.AutoReload ? "reload" : "toast";
        bool debug = plugin?.Configuration?.DebugLoggingState == Configuration.DebugLoggingState.Enabled;

        body.Seek(0, SeekOrigin.Begin);
        using StreamReader reader = new StreamReader(body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: -1, leaveOpen: true);
        string html = await reader.ReadToEndAsync().ConfigureAwait(false);

        int insertPoint = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (insertPoint < 0)
        {
            return;
        }

        string script = GetAutoRefreshScript(mode, debug);
        string modified = string.Concat(html.AsSpan(0, insertPoint), script, html.AsSpan(insertPoint));
        byte[] bytes = Encoding.UTF8.GetBytes(modified);

        body.SetLength(0);
        body.Seek(0, SeekOrigin.Begin);
        await body.WriteAsync(bytes).ConfigureAwait(false);
    }
}
