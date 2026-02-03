using System.Net.Http.Headers;
using System.Text.Json;
using SmarthomeApi.Models;

namespace SmarthomeApi.Services;

public interface IGitHubService
{
    Task<GitHubRepoValidation> ValidateModuleRepoAsync(string repoUrl);
    Task<MfeModuleManifest?> GetMfeManifestAsync(string repoUrl);
    Task<string?> GetFileContentAsync(string repoUrl, string filePath);
}

public class GitHubRepoValidation
{
    public bool IsValid { get; set; }
    public string? Error { get; set; }
    public string? Owner { get; set; }
    public string? Repo { get; set; }
    public bool HasMfeManifest { get; set; }
    public bool HasMfeDeployment { get; set; }
    public bool HasModuleFields { get; set; }
    public bool HasServiceTemplate { get; set; }
}

public class GitHubService : IGitHubService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GitHubService(ILogger<GitHubService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("SmarthomeApi", "1.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
    }

    public async Task<GitHubRepoValidation> ValidateModuleRepoAsync(string repoUrl)
    {
        var validation = new GitHubRepoValidation();

        // Parse the GitHub URL
        var parsed = ParseGitHubUrl(repoUrl);
        if (parsed == null)
        {
            validation.Error = "Invalid GitHub URL. Expected format: https://github.com/owner/repo";
            return validation;
        }

        validation.Owner = parsed.Value.Owner;
        validation.Repo = parsed.Value.Repo;

        try
        {
            // Check for required files in config/ directory
            var configFiles = await GetDirectoryContentsAsync(parsed.Value.Owner, parsed.Value.Repo, "config");

            if (configFiles == null)
            {
                validation.Error = "Repository does not have a 'config' directory";
                return validation;
            }

            foreach (var file in configFiles)
            {
                switch (file.Name?.ToLower())
                {
                    case "mfe-manifest.json":
                        validation.HasMfeManifest = true;
                        break;
                    case "mfe-deployment.yaml":
                        validation.HasMfeDeployment = true;
                        break;
                    case "module-fields.json":
                        validation.HasModuleFields = true;
                        break;
                    case "service-template.yaml":
                        validation.HasServiceTemplate = true;
                        break;
                }
            }

            if (!validation.HasMfeManifest)
            {
                validation.Error = "Repository missing required file: config/mfe-manifest.json";
                return validation;
            }

            if (!validation.HasMfeDeployment)
            {
                validation.Error = "Repository missing required file: config/mfe-deployment.yaml";
                return validation;
            }

            validation.IsValid = true;
            return validation;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to validate GitHub repo: {Url}", repoUrl);
            validation.Error = $"Failed to access repository: {ex.Message}";
            return validation;
        }
    }

    public async Task<MfeModuleManifest?> GetMfeManifestAsync(string repoUrl)
    {
        var content = await GetFileContentAsync(repoUrl, "config/mfe-manifest.json");
        if (content == null) return null;

        try
        {
            return JsonSerializer.Deserialize<MfeModuleManifest>(content, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse mfe-manifest.json from {Url}", repoUrl);
            return null;
        }
    }

    public async Task<string?> GetFileContentAsync(string repoUrl, string filePath)
    {
        var parsed = ParseGitHubUrl(repoUrl);
        if (parsed == null) return null;

        try
        {
            // Use raw.githubusercontent.com for file content
            var rawUrl = $"https://raw.githubusercontent.com/{parsed.Value.Owner}/{parsed.Value.Repo}/main/{filePath}";

            var response = await _httpClient.GetAsync(rawUrl);

            // Try 'master' branch if 'main' fails
            if (!response.IsSuccessStatusCode)
            {
                rawUrl = $"https://raw.githubusercontent.com/{parsed.Value.Owner}/{parsed.Value.Repo}/master/{filePath}";
                response = await _httpClient.GetAsync(rawUrl);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get file {Path} from {Repo}: {Status}",
                    filePath, repoUrl, response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching file {Path} from {Repo}", filePath, repoUrl);
            return null;
        }
    }

    private async Task<List<GitHubContent>?> GetDirectoryContentsAsync(string owner, string repo, string path)
    {
        try
        {
            var url = $"https://api.github.com/repos/{owner}/{repo}/contents/{path}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get directory contents: {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<GitHubContent>>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching directory contents for {Owner}/{Repo}/{Path}", owner, repo, path);
            return null;
        }
    }

    private static (string Owner, string Repo)? ParseGitHubUrl(string url)
    {
        // Handle various GitHub URL formats
        // https://github.com/owner/repo
        // https://github.com/owner/repo.git
        // github.com/owner/repo

        try
        {
            url = url.Trim();

            if (!url.StartsWith("http"))
            {
                url = "https://" + url;
            }

            var uri = new Uri(url);

            if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length < 2)
            {
                return null;
            }

            var owner = segments[0];
            var repo = segments[1].Replace(".git", "");

            return (owner, repo);
        }
        catch
        {
            return null;
        }
    }
}

public class GitHubContent
{
    public string? Name { get; set; }
    public string? Path { get; set; }
    public string? Type { get; set; }
    public string? DownloadUrl { get; set; }
}
