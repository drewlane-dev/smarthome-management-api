using LiteDB;
using SmarthomeApi.Models;

namespace SmarthomeApi.Services;

public interface IDatabaseService : IDisposable
{
    ILiteCollection<InstalledModule> Modules { get; }
    void EnsureMigrated(string jsonFilePath);
}

public class DatabaseService : IDatabaseService
{
    private readonly LiteDatabase _database;
    private readonly ILogger<DatabaseService> _logger;

    public DatabaseService(IWebHostEnvironment env, ILogger<DatabaseService> logger)
    {
        _logger = logger;
        var dbPath = Path.Combine(env.ContentRootPath, "smarthome.db");
        _database = new LiteDatabase(dbPath);

        // Ensure index on Name field for fast lookups
        Modules.EnsureIndex(x => x.Name, unique: true);

        _logger.LogInformation("Database initialized at {Path}", dbPath);
    }

    public ILiteCollection<InstalledModule> Modules => _database.GetCollection<InstalledModule>("modules");

    public void EnsureMigrated(string jsonFilePath)
    {
        // If database already has modules, skip migration
        if (Modules.Count() > 0)
        {
            _logger.LogInformation("Database already contains {Count} modules, skipping migration", Modules.Count());
            return;
        }

        // Check for existing JSON file to migrate
        if (!File.Exists(jsonFilePath))
        {
            _logger.LogInformation("No existing JSON file to migrate");
            return;
        }

        try
        {
            var json = File.ReadAllText(jsonFilePath);
            var modules = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, InstalledModule>>(
                json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (modules == null || modules.Count == 0)
            {
                _logger.LogInformation("No modules found in JSON file");
                return;
            }

            foreach (var module in modules.Values)
            {
                Modules.Insert(module);
            }

            _logger.LogInformation("Migrated {Count} modules from JSON to database", modules.Count);

            // Rename the old JSON file as backup
            var backupPath = jsonFilePath + ".bak";
            File.Move(jsonFilePath, backupPath);
            _logger.LogInformation("Renamed old JSON file to {BackupPath}", backupPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate modules from JSON file");
        }
    }

    public void Dispose()
    {
        _database?.Dispose();
    }
}
