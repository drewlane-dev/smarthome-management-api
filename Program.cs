using SmarthomeApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Bind to all interfaces on port 5000
builder.WebHost.UseUrls("http://*:5000");

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register services
builder.Services.AddSingleton<IKubernetesService, KubernetesService>();
builder.Services.AddSingleton<IGitHubService, GitHubService>();
builder.Services.AddSingleton<IDatabaseService, DatabaseService>();
builder.Services.AddSingleton<IKnownModulesService, KnownModulesService>();
builder.Services.AddSingleton<IModuleService, ModuleService>();

// HttpClient for MFE readiness checks
builder.Services.AddHttpClient("MfeCheck", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});

// Configure CORS - allow all origins for internal network
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("Content-Disposition");
    });
});

var app = builder.Build();

// Enable Swagger in all environments for now
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();

app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
