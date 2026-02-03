namespace SmarthomeApi.Models;

public class MfeManifest
{
    public List<MfeDefinition> Mfes { get; set; } = new();
}

public class MfeDefinition
{
    /// <summary>Route path (e.g., 'spotify', 'lights')</summary>
    public required string Path { get; set; }

    /// <summary>URL to the remote's remoteEntry.json</summary>
    public required string RemoteEntry { get; set; }

    /// <summary>Name of the remote (must match federation.config.js name)</summary>
    public required string RemoteName { get; set; }

    /// <summary>Exposed module path (e.g., './Component')</summary>
    public required string ExposedModule { get; set; }

    /// <summary>Export name of the component (e.g., 'SpotifyComponent')</summary>
    public required string ComponentExport { get; set; }

    /// <summary>Tile configuration for the home screen</summary>
    public required TileConfig Tile { get; set; }
}

public class TileConfig
{
    public required string Label { get; set; }
    public required string Icon { get; set; }
    public required string Color { get; set; }
}
