using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SpenceAI.Application;
using SpenceAI.Application.Categories;
using SpenceAI.Application.Imports;
using SpenceAI.Application.Settings;
using SpenceAI.Infrastructure;
using SpenceAI.Infrastructure.Data;
using SpenceAI.WebApi;
using SpenceAI.WebApi.Endpoints;

// Release builds ship appsettings.Desktop.json next to the executable (see packaging/). Its presence turns on desktop
// mode: content root = the app folder (double-clicking on macOS/Linux starts in the home folder), and the browser opens.
var desktop = DesktopHost.IsDesktop();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = desktop ? AppContext.BaseDirectory : null,
});
if (desktop)
{
    builder.Configuration.AddJsonFile("appsettings.Desktop.json", optional: false, reloadOnChange: false);
}
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// A second launch just reopens the browser on the copy that's already running.
if (desktop && await DesktopHost.TryReuseRunningAsync(builder.Configuration))
{
    return;
}

builder.Services.AddSpenceAi(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = BackupService.MaxUploadBytes);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = BackupService.MaxUploadBytes);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Known, user-facing errors become 4xx ProblemDetails; anything else is logged and returned as a generic 500.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, detail) = error switch
    {
        AppException ex => (ex.StatusCode, ex.Message),
        BadHttpRequestException ex => (ex.StatusCode, "The request couldn't be read. Check the values and try again."),
        _ => (StatusCodes.Status500InternalServerError, "Something went wrong on the server. Check the API logs for details."),
    };

    context.Response.StatusCode = status;
    var extensions = error is AppException { Code: { } code } ? new Dictionary<string, object?> { ["code"] = code } : null;
    await Results.Problem(detail: detail, statusCode: status, extensions: extensions).ExecuteAsync(context);
}));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var storage = scope.ServiceProvider.GetRequiredService<StorageOptions>();

    // An update that changes the schema backs up the old data first, so it can be restored if anything goes wrong.
    if (File.Exists(storage.DatabasePath) && (await db.Database.GetAppliedMigrationsAsync()).Any() && (await db.Database.GetPendingMigrationsAsync()).Any())
    {
        await scope.ServiceProvider.GetRequiredService<BackupService>().CreateAsync(BackupKind.BeforeUpdate, CancellationToken.None);
    }

    await db.Database.MigrateAsync();
    await CategoryTemplates.SeedAsync(db);
    await scope.ServiceProvider.GetRequiredService<SettingsService>().GetEntityAsync();

    app.Logger.LogInformation("Data directory: {DataDirectory}", storage.DataDirectory);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

// The built web app (wwwroot) is served by the API in release builds, so the API calls are same-origin.
var hasWebApp = File.Exists(Path.Combine(app.Environment.WebRootPath ?? "", "index.html"));
if (hasWebApp)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapTransactionEndpoints();
app.MapCategoryEndpoints();
app.MapAccountEndpoints();
app.MapQuickAddEndpoints();
app.MapSavingsEndpoints();
app.MapBillEndpoints();
app.MapAppEndpoints();
app.MapBackupEndpoints();

if (hasWebApp)
{
    app.MapFallback("/api/{**rest}", () => Results.NotFound());
    app.MapFallbackToFile("index.html");
}

if (desktop)
{
    DesktopHost.OpenBrowserWhenStarted(app);
}

app.Run();
