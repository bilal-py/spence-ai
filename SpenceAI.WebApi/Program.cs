using System.IO;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SpenceAI.Application.Common.Interfaces;
using SpenceAI.Application.Services;
using SpenceAI.Infrastructure.Data;
using SpenceAI.Infrastructure.Data.Repositories;
using SpenceAI.Infrastructure.Services;
using SpenceAI.WebApi.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Read storage mode from local file (default to Local if file doesn't exist)
string storageModeFile = Path.Combine(Directory.GetCurrentDirectory(), "storage-mode.txt");
string storageMode = "Local"; // Default to Local
if (File.Exists(storageModeFile))
{
    storageMode = File.ReadAllText(storageModeFile).Trim();
}

// Configure DbContext based on storage mode
if (storageMode.Equals("Local", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlite("Data Source=spence.db"));
}
else
{
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
}

builder.Services.AddScoped<IApplicationDbContext>(provider =>
    provider.GetRequiredService<ApplicationDbContext>());

builder.Services.AddHttpClient();
builder.Services.AddHttpClient<IAiCategorizationService, GeminiCategorizationService>();
builder.Services.AddScoped<IPdfExtractionService, PdfExtractionService>();
builder.Services.AddScoped<IEncryptionService, EncryptionService>; // Changed to scoped
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<ISyncEngineService, SyncEngineService>; // Added SyncEngineService
builder.Services.AddScoped<ExpenseProcessingService>();

builder.Services.AddControllers();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

builder.Services.AddScoped<IExpenseRepository, ExpenseRepository>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy
            .WithOrigins(
                "http://localhost:5173",
                "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();

    if (app.Environment.IsDevelopment())
    {
        await DevelopmentDataSeeder.SeedAsync(db);
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.MapControllers();
app.MapExpenseEndpoints();

app.Run();