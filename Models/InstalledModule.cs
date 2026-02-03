using LiteDB;

namespace SmarthomeApi.Models;

/// <summary>
/// An installed module from a GitHub repository
/// </summary>
public class InstalledModule
{
    [BsonId]
    public required string Name { get; set; }
    public required string DisplayName { get; set; }
    public string? Description { get; set; }
    public required string RepoUrl { get; set; }
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;

    // MFE configuration
    public required string RemoteEntry { get; set; }
    public required string RemoteName { get; set; }
    public required string ExposedModule { get; set; }
    public required string ComponentExport { get; set; }
    public required TileConfig Tile { get; set; }
    public int NodePort { get; set; }

    // Optional fields configuration (loaded from module-fields.json)
    public List<ModuleField> Fields { get; set; } = new();

    // Optional service template (loaded from service-template.yaml)
    public string? ServiceTemplate { get; set; }

    // Deployment state
    public bool MfeDeployed { get; set; }
    public bool ServiceDeployed { get; set; }
    public Dictionary<string, string> ServiceFieldValues { get; set; } = new();
}

/// <summary>
/// Request to install a module from GitHub
/// </summary>
public class InstallModuleRequest
{
    public required string RepoUrl { get; set; }
}

/// <summary>
/// Response from module installation
/// </summary>
public class InstallModuleResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? ModuleName { get; set; }
    public string? DisplayName { get; set; }
    public bool RequiresConfiguration { get; set; }
    public List<ModuleField>? Fields { get; set; }
}
