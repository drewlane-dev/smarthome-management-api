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
    private readonly ILogger<ModulesController> _logger;

    public ModulesController(
        IModuleService moduleService,
        IGitHubService gitHubService,
        IKnownModulesService knownModulesService,
        ILogger<ModulesController> logger)
    {
        _moduleService = moduleService;
        _gitHubService = gitHubService;
        _knownModulesService = knownModulesService;
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
}
