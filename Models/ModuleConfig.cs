namespace SmarthomeApi.Models;

/// <summary>
/// MFE manifest from mfe-manifest.json
/// </summary>
public class MfeModuleManifest
{
    public required string Name { get; set; }
    public required string DisplayName { get; set; }
    public string? Description { get; set; }
    public required string RemoteEntry { get; set; }
    public required string RemoteName { get; set; }
    public required string ExposedModule { get; set; }
    public required string ComponentExport { get; set; }
    public required TileConfig Tile { get; set; }
    public int NodePort { get; set; }
}

/// <summary>
/// Module fields from module-fields.json
/// </summary>
public class ModuleFieldsConfig
{
    public List<ModuleField> Fields { get; set; } = new();
}

/// <summary>
/// A form field definition for module deployment
/// </summary>
public class ModuleField
{
    public required string Name { get; set; }
    public required string Label { get; set; }
    public string Type { get; set; } = "text"; // text, number, password, select
    public bool Required { get; set; } = true;
    public string? DefaultValue { get; set; }
    public string? Description { get; set; }
    public List<string>? Options { get; set; } // For select type
}

/// <summary>
/// Combined module definition loaded from separate files
/// </summary>
public class ModuleDefinition
{
    public required string Name { get; set; }
    public required string DisplayName { get; set; }
    public string? Description { get; set; }
    public List<ModuleField> Fields { get; set; } = new();

    // File paths for YAML templates
    public string? MfeDeploymentYaml { get; set; }
    public string? ServiceTemplateYaml { get; set; }

    // MFE configuration
    public required ModuleMfeConfig Mfe { get; set; }
}

/// <summary>
/// MFE configuration for a module
/// </summary>
public class ModuleMfeConfig
{
    public required string RemoteEntry { get; set; }
    public required string RemoteName { get; set; }
    public required string ExposedModule { get; set; }
    public required string ComponentExport { get; set; }
    public required TileConfig Tile { get; set; }
}

/// <summary>
/// Request to deploy a module
/// </summary>
public class DeployModuleRequest
{
    public Dictionary<string, string> FieldValues { get; set; } = new();
}

/// <summary>
/// A deployed module instance
/// </summary>
public class DeployedModule
{
    public required string ModuleName { get; set; }
    public Dictionary<string, string> FieldValues { get; set; } = new();
    public DateTime DeployedAt { get; set; } = DateTime.UtcNow;
    public bool MfeDeployed { get; set; }
    public bool ServiceDeployed { get; set; }
}

/// <summary>
/// Response with module status
/// </summary>
public class ModuleStatusResponse
{
    public required string ModuleName { get; set; }
    public bool IsDeployed { get; set; }
    public bool IsRunning { get; set; }
    public bool ServiceDeployed { get; set; }
    public bool ServiceRunning { get; set; }
    public string? PodStatus { get; set; }
    public string? MfePodStatus { get; set; }
    public Dictionary<string, string>? FieldValues { get; set; }
    public string? Message { get; set; }
}
