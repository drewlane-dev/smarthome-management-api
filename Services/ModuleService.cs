using System.Text.Json;
using SmarthomeApi.Models;

namespace SmarthomeApi.Services;

public interface IModuleService
{
    IEnumerable<InstalledModule> GetInstalledModules();
    InstalledModule? GetInstalledModule(string name);
    Task<InstallModuleResponse> InstallModuleAsync(string repoUrl);
    Task<bool> UninstallModuleAsync(string name);
    Task<ModuleStatusResponse> ConfigureModuleAsync(string name, DeployModuleRequest request);
    Task<ModuleStatusResponse> GetModuleStatusAsync(string name);
    MfeManifest GetMfeManifest();
}

public class ModuleService : IModuleService
{
    private readonly IKubernetesService _kubernetesService;
    private readonly IGitHubService _gitHubService;
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<ModuleService> _logger;
    private readonly IConfiguration _configuration;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ModuleService(
        IKubernetesService kubernetesService,
        IGitHubService gitHubService,
        IDatabaseService databaseService,
        ILogger<ModuleService> logger,
        IConfiguration configuration,
        IWebHostEnvironment env)
    {
        _kubernetesService = kubernetesService;
        _gitHubService = gitHubService;
        _databaseService = databaseService;
        _logger = logger;
        _configuration = configuration;

        // Migrate from old JSON file if needed
        var jsonFilePath = Path.Combine(env.ContentRootPath, "installed-modules.json");
        _databaseService.EnsureMigrated(jsonFilePath);
    }

    public IEnumerable<InstalledModule> GetInstalledModules() => _databaseService.Modules.FindAll();

    public InstalledModule? GetInstalledModule(string name) => _databaseService.Modules.FindById(name);

    public async Task<InstallModuleResponse> InstallModuleAsync(string repoUrl)
    {
        _logger.LogInformation("Installing module from: {Url}", repoUrl);

        // Validate the repository
        var validation = await _gitHubService.ValidateModuleRepoAsync(repoUrl);
        if (!validation.IsValid)
        {
            return new InstallModuleResponse
            {
                Success = false,
                Error = validation.Error
            };
        }

        // Fetch the MFE manifest
        var mfeManifest = await _gitHubService.GetMfeManifestAsync(repoUrl);
        if (mfeManifest == null)
        {
            return new InstallModuleResponse
            {
                Success = false,
                Error = "Failed to parse mfe-manifest.json"
            };
        }

        // Check if module is already installed
        if (_databaseService.Modules.FindById(mfeManifest.Name) != null)
        {
            return new InstallModuleResponse
            {
                Success = false,
                Error = $"Module '{mfeManifest.Name}' is already installed"
            };
        }

        // Fetch optional module fields
        var fields = new List<ModuleField>();
        if (validation.HasModuleFields)
        {
            var fieldsJson = await _gitHubService.GetFileContentAsync(repoUrl, "config/module-fields.json");
            if (fieldsJson != null)
            {
                try
                {
                    var fieldsConfig = JsonSerializer.Deserialize<ModuleFieldsConfig>(fieldsJson, JsonOptions);
                    if (fieldsConfig != null)
                    {
                        fields = fieldsConfig.Fields;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse module-fields.json");
                }
            }
        }

        // Fetch optional service template
        string? serviceTemplate = null;
        if (validation.HasServiceTemplate)
        {
            serviceTemplate = await _gitHubService.GetFileContentAsync(repoUrl, "config/service-template.yaml");
        }

        // Fetch MFE deployment YAML
        var mfeDeploymentYaml = await _gitHubService.GetFileContentAsync(repoUrl, "config/mfe-deployment.yaml");
        if (mfeDeploymentYaml == null)
        {
            return new InstallModuleResponse
            {
                Success = false,
                Error = "Failed to fetch mfe-deployment.yaml"
            };
        }

        // Try to deploy the MFE container
        var mfeDeployed = false;
        int mfeNodePort = 0;
        string? deployError = null;

        try
        {
            mfeDeployed = await _kubernetesService.ApplyYamlAsync(mfeDeploymentYaml);
            if (!mfeDeployed)
            {
                deployError = "Kubernetes returned failure when applying MFE deployment";
                _logger.LogWarning("Failed to deploy MFE container for {Name}", mfeManifest.Name);
            }
            else
            {
                // Get the assigned NodePort from the MFE service
                var serviceName = $"{mfeManifest.Name}-mfe";
                var nodePort = await _kubernetesService.GetServiceNodePortAsync(serviceName);
                if (nodePort.HasValue)
                {
                    mfeNodePort = nodePort.Value;
                    _logger.LogInformation("MFE service {Name} assigned NodePort: {Port}", serviceName, mfeNodePort);
                }
            }
        }
        catch (Exception ex)
        {
            deployError = $"Kubernetes error: {ex.Message}";
            _logger.LogError(ex, "Exception deploying MFE container for {Name}", mfeManifest.Name);
        }

        // Create installed module record (save even if K8s deployment failed)
        var installedModule = new InstalledModule
        {
            Name = mfeManifest.Name,
            DisplayName = mfeManifest.DisplayName,
            Description = mfeManifest.Description,
            RepoUrl = repoUrl,
            RemoteEntry = mfeManifest.RemoteEntry,
            RemoteName = mfeManifest.RemoteName,
            ExposedModule = mfeManifest.ExposedModule,
            ComponentExport = mfeManifest.ComponentExport,
            Tile = mfeManifest.Tile,
            Fields = fields,
            ServiceTemplate = serviceTemplate,
            MfeDeployed = mfeDeployed,
            MfeNodePort = mfeNodePort,
            ServiceDeployed = false
        };

        _databaseService.Modules.Insert(installedModule);

        _logger.LogInformation("Module installed: {Name} from {Url} (MFE deployed: {Deployed})",
            mfeManifest.Name, repoUrl, mfeDeployed);

        return new InstallModuleResponse
        {
            Success = true,
            ModuleName = mfeManifest.Name,
            DisplayName = mfeManifest.DisplayName,
            RequiresConfiguration = fields.Count > 0 && !string.IsNullOrEmpty(serviceTemplate),
            Fields = fields.Count > 0 ? fields : null,
            Error = deployError != null ? $"Module registered but K8s deployment failed: {deployError}" : null
        };
    }

    public async Task<bool> UninstallModuleAsync(string name)
    {
        var module = _databaseService.Modules.FindById(name);
        if (module == null)
        {
            _logger.LogWarning("Cannot uninstall: module '{Name}' not found", name);
            return false;
        }

        var success = true;

        // Delete service deployment if it was deployed
        if (module.ServiceDeployed)
        {
            success &= await _kubernetesService.DeleteDeploymentAsync(name);
        }

        // Delete MFE deployment
        if (module.MfeDeployed)
        {
            success &= await _kubernetesService.DeleteDeploymentAsync($"{name}-mfe");
        }

        if (success)
        {
            _databaseService.Modules.Delete(name);
            _logger.LogInformation("Module uninstalled: {Name}", name);
        }

        return success;
    }

    public async Task<ModuleStatusResponse> ConfigureModuleAsync(string name, DeployModuleRequest request)
    {
        var module = _databaseService.Modules.FindById(name);
        if (module == null)
        {
            return new ModuleStatusResponse
            {
                ModuleName = name,
                IsDeployed = false,
                Message = $"Module '{name}' not found"
            };
        }

        // If no service template, nothing to configure
        if (string.IsNullOrEmpty(module.ServiceTemplate))
        {
            return new ModuleStatusResponse
            {
                ModuleName = name,
                IsDeployed = true,
                IsRunning = true,
                Message = "Module has no service to configure"
            };
        }

        // Validate required fields
        foreach (var field in module.Fields.Where(f => f.Required))
        {
            if (!request.FieldValues.TryGetValue(field.Name, out var value) || string.IsNullOrWhiteSpace(value))
            {
                if (!string.IsNullOrEmpty(field.DefaultValue))
                {
                    request.FieldValues[field.Name] = field.DefaultValue;
                }
                else
                {
                    return new ModuleStatusResponse
                    {
                        ModuleName = name,
                        IsDeployed = false,
                        Message = $"Required field '{field.Name}' is missing"
                    };
                }
            }
        }

        // Apply template values and deploy
        var serviceYaml = ApplyTemplateValues(module.ServiceTemplate, request.FieldValues);
        var success = await _kubernetesService.ApplyYamlAsync(serviceYaml);

        if (!success)
        {
            return new ModuleStatusResponse
            {
                ModuleName = name,
                IsDeployed = false,
                Message = "Failed to deploy service"
            };
        }

        // Get the assigned NodePort from the service
        var serviceNodePort = await _kubernetesService.GetServiceNodePortAsync(name);
        if (serviceNodePort.HasValue)
        {
            module.ServiceNodePort = serviceNodePort.Value;
            _logger.LogInformation("Service {Name} assigned NodePort: {Port}", name, serviceNodePort.Value);
        }

        // Update module state
        module.ServiceDeployed = true;
        module.ServiceFieldValues = request.FieldValues;
        _databaseService.Modules.Update(module);

        _logger.LogInformation("Module configured: {Name}", name);

        return new ModuleStatusResponse
        {
            ModuleName = name,
            IsDeployed = true,
            IsRunning = false,  // Not running yet - need to poll for pod status
            ServiceDeployed = true,
            ServiceRunning = false,  // Not running yet - need to poll for pod status
            FieldValues = request.FieldValues
        };
    }

    public async Task<ModuleStatusResponse> GetModuleStatusAsync(string name)
    {
        var module = _databaseService.Modules.FindById(name);
        if (module == null)
        {
            return new ModuleStatusResponse
            {
                ModuleName = name,
                IsDeployed = false,
                Message = $"Module '{name}' not found"
            };
        }

        // Get pod statuses
        var mfePodStatus = await _kubernetesService.GetPodStatusAsync($"{name}-mfe");
        var servicePodStatus = module.ServiceDeployed
            ? await _kubernetesService.GetPodStatusAsync(name)
            : null;

        return new ModuleStatusResponse
        {
            ModuleName = name,
            IsDeployed = module.MfeDeployed,
            IsRunning = mfePodStatus?.IsRunning ?? false,
            ServiceDeployed = module.ServiceDeployed,
            ServiceRunning = servicePodStatus?.IsRunning ?? false,
            PodStatus = servicePodStatus?.Status,
            MfePodStatus = mfePodStatus?.Status,
            FieldValues = module.ServiceFieldValues,
            ServiceNodePort = module.ServiceNodePort > 0 ? module.ServiceNodePort : null
        };
    }

    public MfeManifest GetMfeManifest()
    {
        var manifest = new MfeManifest();

        foreach (var module in _databaseService.Modules.FindAll())
        {
            if (!module.MfeDeployed) continue;

            manifest.Mfes.Add(new MfeDefinition
            {
                Path = module.Name,
                RemoteEntry = module.RemoteEntry,
                RemoteName = module.RemoteName,
                ExposedModule = module.ExposedModule,
                ComponentExport = module.ComponentExport,
                Tile = module.Tile,
                NodePort = module.MfeNodePort
            });
        }

        return manifest;
    }

    private static string ApplyTemplateValues(string template, Dictionary<string, string> values)
    {
        var result = template;
        foreach (var (key, value) in values)
        {
            result = result.Replace($"{{{{{key}}}}}", value);
        }
        return result;
    }
}
