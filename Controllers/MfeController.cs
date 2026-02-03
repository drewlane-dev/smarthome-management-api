using Microsoft.AspNetCore.Mvc;
using SmarthomeApi.Models;
using SmarthomeApi.Services;

namespace SmarthomeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MfeController : ControllerBase
{
    private readonly IModuleService _moduleService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MfeController> _logger;

    public MfeController(
        IModuleService moduleService,
        IConfiguration configuration,
        ILogger<MfeController> logger)
    {
        _moduleService = moduleService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Get the MFE manifest with all deployed micro-frontends
    /// </summary>
    [HttpGet("manifest")]
    public ActionResult<MfeManifest> GetManifest()
    {
        var manifest = _moduleService.GetMfeManifest();

        // Determine the host to use for MFE URLs
        // Use MfeBaseUrl config if set, otherwise detect from request
        var mfeBaseUrl = _configuration["MfeBaseUrl"];

        string host;
        if (!string.IsNullOrEmpty(mfeBaseUrl))
        {
            host = mfeBaseUrl;
        }
        else
        {
            // Use the request host - this works for both local (localhost) and prod (Pi IP)
            host = $"http://{Request.Host.Host}";
        }

        // Build full URLs for each MFE using dynamic NodePorts
        foreach (var mfe in manifest.Mfes)
        {
            mfe.RemoteEntry = BuildFullUrl(mfe.NodePort, mfe.RemoteEntry, host);
        }

        _logger.LogDebug("Returning MFE manifest with {Count} deployed modules (host={Host})",
            manifest.Mfes.Count, host);
        return Ok(manifest);
    }

    private string BuildFullUrl(int nodePort, string relativePath, string baseHost)
    {
        // If already a full URL, return as-is
        if (relativePath.StartsWith("http://") || relativePath.StartsWith("https://"))
        {
            return relativePath;
        }

        // Ensure relative path starts with /
        if (!relativePath.StartsWith("/"))
        {
            relativePath = "/" + relativePath;
        }

        if (nodePort <= 0)
        {
            _logger.LogWarning("Module has no NodePort assigned, using path only: {Path}", relativePath);
            return relativePath;
        }

        return $"{baseHost}:{nodePort}{relativePath}";
    }
}
