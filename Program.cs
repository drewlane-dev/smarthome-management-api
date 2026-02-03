using SmarthomeApi.Services;

var builder = WebApplication.CreateBuilder(args);

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
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// Enable Swagger in all environments for now
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowAll");

app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
