using Microsoft.AspNetCore.Mvc;
using SmarthomeApi.Models;
using SmarthomeApi.Services;

namespace SmarthomeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ModulesController : ControllerBase
{
    private readonly IModuleService _moduleService;
    private readonly IGitHubService _gitHubService;
    private readonly IKnownModulesService _knownModulesService;
    private readonly IKubernetesService _kubernetesService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ModulesController> _logger;

    public ModulesController(
        IModuleService moduleService,
        IGitHubService gitHubService,
        IKnownModulesService knownModulesService,
        IKubernetesService kubernetesService,
        IHttpClientFactory httpClientFactory,
        ILogger<ModulesController> logger)
    {
        _moduleService = moduleService;
        _gitHubService = gitHubService;
        _knownModulesService = knownModulesService;
        _kubernetesService = kubernetesService;
        _httpClient = httpClientFactory.CreateClient("MfeCheck");
        _logger = logger;
    }

    /// <summary>
    /// Get all installed modules
    /// </summary>
    [HttpGet]
    public ActionResult<IEnumerable<InstalledModule>> GetModules()
    {
        return Ok(_moduleService.GetInstalledModules());
    }

    /// <summary>
    /// Get all known (pre-approved) modules available for installation
    /// </summary>
    [HttpGet("known")]
    public async Task<ActionResult<IEnumerable<KnownModule>>> GetKnownModules()
    {
        var knownModules = await _knownModulesService.GetKnownModulesAsync();
        var installedNames = _moduleService.GetInstalledModules().Select(m => m.Name).ToHashSet();

        // Return known modules that aren't already installed
        var available = knownModules.Where(km => !installedNames.Contains(km.Name));
        return Ok(available);
    }

    /// <summary>
    /// Get a specific installed module
    /// </summary>
    [HttpGet("{name}")]
    public ActionResult<InstalledModule> GetModule(string name)
    {
        var module = _moduleService.GetInstalledModule(name);
        if (module == null)
            return NotFound(new { message = $"Module '{name}' not found" });

        return Ok(module);
    }

    /// <summary>
    /// Validate a GitHub repository for module installation
    /// </summary>
    [HttpPost("validate")]
    public async Task<ActionResult<GitHubRepoValidation>> ValidateRepo([FromBody] InstallModuleRequest request)
    {
        var validation = await _gitHubService.ValidateModuleRepoAsync(request.RepoUrl);
        return Ok(validation);
    }

    /// <summary>
    /// Install a module from a GitHub repository
    /// </summary>
    [HttpPost("install")]
    public async Task<ActionResult<InstallModuleResponse>> InstallModule([FromBody] InstallModuleRequest request)
    {
        _logger.LogInformation("Installing module from: {Url}", request.RepoUrl);
        var result = await _moduleService.InstallModuleAsync(request.RepoUrl);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// Uninstall a module
    /// </summary>
    [HttpDelete("{name}")]
    public async Task<ActionResult> UninstallModule(string name)
    {
        _logger.LogInformation("Uninstalling module: {Name}", name);
        var success = await _moduleService.UninstallModuleAsync(name);

        if (!success)
            return StatusCode(500, new { message = $"Failed to uninstall module '{name}'" });

        return Ok(new { message = $"Module '{name}' uninstalled successfully" });
    }

    /// <summary>
    /// Get the status of a module
    /// </summary>
    [HttpGet("{name}/status")]
    public async Task<ActionResult<ModuleStatusResponse>> GetModuleStatus(string name)
    {
        var status = await _moduleService.GetModuleStatusAsync(name);
        return Ok(status);
    }

    /// <summary>
    /// Configure a module's service (deploy with field values)
    /// </summary>
    [HttpPost("{name}/configure")]
    public async Task<ActionResult<ModuleStatusResponse>> ConfigureModule(
        string name,
        [FromBody] DeployModuleRequest request)
    {
        _logger.LogInformation("Configuring module: {Name}", name);
        var result = await _moduleService.ConfigureModuleAsync(name, request);

        if (!result.IsDeployed)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// Check if a module's MFE is ready (remoteEntry is downloadable)
    /// </summary>
    [HttpGet("{name}/mfe-ready")]
    public async Task<ActionResult<MfeReadyResponse>> CheckMfeReady(string name)
    {
        var module = _moduleService.GetInstalledModule(name);
        if (module == null)
        {
            return Ok(new MfeReadyResponse { Ready = false, Message = "Module not found" });
        }

        if (!module.MfeDeployed || module.MfeNodePort <= 0)
        {
            return Ok(new MfeReadyResponse { Ready = false, Message = "MFE not deployed" });
        }

        // Build the remoteEntry URL using the request host
        var host = Request.Host.Host;
        var remoteEntryPath = module.RemoteEntry.TrimStart('/');
        var url = $"http://{host}:{module.MfeNodePort}/{remoteEntryPath}";

        try
        {
            _logger.LogDebug("Checking MFE readiness at {Url}", url);
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                return Ok(new MfeReadyResponse { Ready = true, Url = url });
            }

            return Ok(new MfeReadyResponse
            {
                Ready = false,
                Message = $"HTTP {(int)response.StatusCode}",
                Url = url
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug("MFE not ready at {Url}: {Error}", url, ex.Message);
            return Ok(new MfeReadyResponse
            {
                Ready = false,
                Message = "Connection failed",
                Url = url
            });
        }
        catch (TaskCanceledException)
        {
            return Ok(new MfeReadyResponse
            {
                Ready = false,
                Message = "Request timeout",
                Url = url
            });
        }
    }

    /// <summary>
    /// Check if a module's service is ready (pod running with NodePort)
    /// </summary>
    [HttpGet("{name}/service-ready")]
    public async Task<ActionResult<ServiceReadyResponse>> CheckServiceReady(string name)
    {
        var module = _moduleService.GetInstalledModule(name);
        if (module == null)
        {
            return Ok(new ServiceReadyResponse { Ready = false, Message = "Module not found" });
        }

        if (!module.ServiceDeployed)
        {
            return Ok(new ServiceReadyResponse { Ready = false, Message = "Service not deployed" });
        }

        // Check pod status
        var podStatus = await _kubernetesService.GetPodStatusAsync(name);
        if (podStatus == null)
        {
            return Ok(new ServiceReadyResponse
            {
                Ready = false,
                PodStatus = "NotFound",
                Message = "Pod not found"
            });
        }

        // Check if pod is running
        if (!podStatus.IsRunning)
        {
            return Ok(new ServiceReadyResponse
            {
                Ready = false,
                PodStatus = podStatus.Status,
                Message = $"Pod is {podStatus.Status}"
            });
        }

        // Get NodePort if not already stored
        var nodePort = module.ServiceNodePort;
        if (nodePort <= 0)
        {
            nodePort = await _kubernetesService.GetServiceNodePortAsync(name) ?? 0;
        }

        if (nodePort <= 0)
        {
            return Ok(new ServiceReadyResponse
            {
                Ready = false,
                PodStatus = podStatus.Status,
                Message = "NodePort not assigned yet"
            });
        }

        return Ok(new ServiceReadyResponse
        {
            Ready = true,
            PodStatus = podStatus.Status,
            NodePort = nodePort
        });
    }

    /// <summary>
    /// Get logs from a module's service pod
    /// </summary>
    [HttpGet("{name}/logs")]
    public async Task<ActionResult<PodLogsResponse>> GetPodLogs(
        string name,
        [FromQuery] int? sinceSeconds = null,
        [FromQuery] int? tailLines = null)
    {
        var module = _moduleService.GetInstalledModule(name);
        if (module == null)
        {
            return Ok(new PodLogsResponse
            {
                Success = false,
                Error = "Module not found"
            });
        }

        if (!module.ServiceDeployed)
        {
            return Ok(new PodLogsResponse
            {
                Success = false,
                Error = "Service not deployed"
            });
        }

        var result = await _kubernetesService.GetPodLogsAsync(name, sinceSeconds, tailLines);

        return Ok(new PodLogsResponse
        {
            Success = result.Success,
            Logs = result.Logs,
            Timestamp = result.Timestamp,
            Error = result.Error
        });
    }
}

public class MfeReadyResponse
{
    public bool Ready { get; set; }
    public string? Message { get; set; }
    public string? Url { get; set; }
}

public class ServiceReadyResponse
{
    public bool Ready { get; set; }
    public string? PodStatus { get; set; }
    public int? NodePort { get; set; }
    public string? Message { get; set; }
}

public class PodLogsResponse
{
    public bool Success { get; set; }
    public string? Logs { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Error { get; set; }
}
