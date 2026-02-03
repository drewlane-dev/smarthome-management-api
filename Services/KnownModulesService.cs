using System.Text.Json;
using SmarthomeApi.Models;

namespace SmarthomeApi.Services;

public interface IKnownModulesService
{
    Task<IEnumerable<KnownModule>> GetKnownModulesAsync();
}

public class KnownModulesService(
    IGitHubService gitHubService,
    IConfiguration configuration,
    ILogger<KnownModulesService> logger) : IKnownModulesService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Cache to avoid hitting GitHub on every request
    private List<KnownModule>? _cachedModules;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<IEnumerable<KnownModule>> GetKnownModulesAsync()
    {
        // Return cached data if still valid
        if (_cachedModules != null && DateTime.UtcNow < _cacheExpiry)
        {
            return _cachedModules;
        }

        var repoUrl = configuration["KnownModulesRepoUrl"];
        if (string.IsNullOrEmpty(repoUrl))
        {
            logger.LogWarning("KnownModulesRepoUrl not configured, returning empty list");
            return [];
        }

        try
        {
            logger.LogInformation("Fetching known modules from {Url}", repoUrl);
            var json = await gitHubService.GetFileContentAsync(repoUrl, "known-modules.json");

            if (string.IsNullOrEmpty(json))
            {
                logger.LogWarning("Failed to fetch known-modules.json from {Url}", repoUrl);
                return _cachedModules ?? [];
            }

            var config = JsonSerializer.Deserialize<KnownModulesConfig>(json, JsonOptions);

            _cachedModules = config?.Modules ?? [];
            _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);

            logger.LogInformation("Loaded {Count} known modules", _cachedModules.Count);
            return _cachedModules;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch known modules from {Url}", repoUrl);

            // Return cached data if available, even if expired
            if (_cachedModules != null)
            {
                logger.LogWarning("Returning stale cached modules");
                return _cachedModules;
            }

            return [];
        }
    }
}

public class KnownModulesConfig
{
    public List<KnownModule> Modules { get; set; } = [];
}
