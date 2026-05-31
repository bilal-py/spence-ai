using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SpenceAI.Application.Services;
using SpenceAI.Infrastructure.Services;
using SpenceAI.Application.Common.Interfaces;

namespace SpenceAI.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly SettingsService _settingsService;
    private readonly ISyncEngineService _syncEngineService;
    private readonly IConfiguration _configuration;

    public SettingsController(SettingsService settingsService, ISyncEngineService syncEngineService, IConfiguration configuration)
    {
        _settingsService = settingsService;
        _syncEngineService = syncEngineService;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> GetSettings()
    {
        var settings = await _settingsService.GetSettingsAsync();
        return Ok(settings);
    }

    [HttpPost("update")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.StorageMode) || string.IsNullOrWhiteSpace(request.SelectedAiProvider))
        {
            return BadRequest(new { message = "Storage mode and AI provider are required." });
        }

        await _settingsService.UpdateSettingsAsync(request.StorageMode, request.SelectedAiProvider, request.ApiKey);
        return Ok(new { message = "Settings updated successfully." });
    }

    [HttpPost("sync")]
    public async Task<IActionResult> TriggerSync()
    {
        // Retrieve the cloud connection string from configuration (secrets)
        var cloudConnectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cloudConnectionString))
        {
            // Fallback to environment variable
            cloudConnectionString = Environment.GetEnvironmentVariable("NEON_CONNECTION_STRING");
        }

        if (string.IsNullOrWhiteSpace(cloudConnectionString))
        {
            return BadRequest(new { message = "Cloud connection string not configured." });
        }

        await _syncEngineService.ExecuteManualSyncAsync(cloudConnectionString);
        return Ok(new { message = "Database synchronization completed successfully." });
    }
}

public class UpdateSettingsRequest
{
    public string StorageMode { get; set; } = string.Empty;
    public string SelectedAiProvider { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
}