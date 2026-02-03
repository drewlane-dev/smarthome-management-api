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
builder.Services.AddSingleton<IModuleService, ModuleService>();

// Configure CORS for the touchscreen UI and micro-frontends
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowUI", policy =>
    {
        policy.WithOrigins(
                "http://localhost:4200",      // Shell dev server
                "http://localhost:4201",      // Spotify MFE dev server
                "http://localhost:4202",      // Terraria MFE dev server
                "http://localhost:5000",      // API dev server
                "http://localhost:8200",      // Nginx on Pi
                "http://192.168.4.37:8200",   // Pi IP - shell
                "http://192.168.4.37:30201",  // Pi IP - spotify MFE
                "http://192.168.4.37:30202"   // Pi IP - terraria MFE
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// Enable Swagger in all environments for now
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowUI");

app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
