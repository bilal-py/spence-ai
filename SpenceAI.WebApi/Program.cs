using Microsoft.EntityFrameworkCore;
using SpenceAI.Application.Common.Interfaces;
using SpenceAI.Infrastructure.Data;
using SpenceAI.Infrastructure.Data.Repositories;
using SpenceAI.WebApi.Configuration;
using SpenceAI.WebApi.Endpoints;

var builder = WebApplication.CreateBuilder(args);

var connectionString = NeonConnection.Resolve(builder.Configuration)
    ?? throw new InvalidOperationException(
        "No database connection configured. Add ConnectionStrings:DefaultConnection in user secrets, " +
        "appsettings.Development.json, or set the DATABASE_URL environment variable. " +
        "See appsettings.Development.example.json and https://neon.tech/docs/guides/dotnet-entity-framework");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

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
app.MapExpenseEndpoints();

app.Run();
