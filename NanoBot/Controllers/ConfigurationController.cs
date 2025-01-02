using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NanoBot.Configuration;
using NanoBot.Services;
using NanoBot.Util;
using NanoWakeWord;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace NanoBot.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigurationController : ControllerBase
{    
    private readonly IDynamicOptions<AppConfig> _appConfigOptions;
    private readonly ILogger<ConfigurationController> _logger;
    private readonly string _userSettingsPath;
    private readonly IVoiceService _voiceService;
    private readonly IConfiguration _configuration;

    public ConfigurationController(
        IDynamicOptions<AppConfig> appConfigOptions, 
        ILogger<ConfigurationController> logger, 
        IVoiceService voiceService,
        IConfiguration configuration)
    {
        _appConfigOptions = appConfigOptions;
        _logger = logger;
        _voiceService = voiceService;
        _userSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.user.json");
        _configuration = configuration;
    }
   
    [HttpGet]
    [Route("GetSchema")]
    public async Task<IActionResult> GetSchema(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug($"{nameof(GetSchema)}");

        try
        {
            var schema = ConfigSchemaGenerator.GetSchema<AppConfig>();
            var schemaStr = schema.ToString();
     
            // Deserialize the schema into a JObject for manipulation
            var schemaJson = JsonConvert.DeserializeObject<JObject>(schemaStr);

            // Fetch available voices
            var voiceProvider = _appConfigOptions.Value.VoiceService.TextToSpeechServiceProvider;
            var voices = await _voiceService.GetAvailableVoicesAsync(voiceProvider, cancellationToken);
            var voiceNames = voices.Select(t => t.ShortName).ToArray();
            
            // Update schema
            var agentsConfig = NavigateToProperty(schemaJson, "properties", "Agents", "items", "properties");
            if (agentsConfig != null && agentsConfig["SpeechSynthesisVoiceName"] != null)
                agentsConfig["SpeechSynthesisVoiceName"]["enum"] = JArray.FromObject(voiceNames);

            // Fetch available wake words
            var wakeWordModels = WakeWordUtil.GetModels();

            // Update schema
            if (agentsConfig != null && agentsConfig["WakeWord"] != null)
                agentsConfig["WakeWord"]["enum"] = JArray.FromObject(wakeWordModels);

            schemaStr = schemaJson.ToString(Formatting.Indented);
            
            return Ok(schemaStr);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating schema for AppConfig");
            return StatusCode(500, "Error generating schema");
        }
    }

    [HttpGet]
    [Route("GetSettings")]
    public IActionResult GetSettings()
    {
        _logger.LogDebug($"{nameof(GetSettings)}");

        try
        {
            var appConfig = _appConfigOptions.Value;

            var settings = new JsonSerializerSettings {
                Formatting = Formatting.Indented
            };
            settings.Converters.Add(new StringEnumConverter());

            var json = JsonConvert.SerializeObject(appConfig, settings);

            return Content(json, "application/json");            
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading settings from IConfiguration");
            return StatusCode(500, "Error reading settings");
        }
    }

    [HttpPost]
    [Route("UpdateSettings")]
    public IActionResult UpdateSettings([FromBody] object updatedSettings)
    {
        _logger.LogDebug($"{nameof(UpdateSettings)}");

        try
        {
            // Serialize the updated settings and overwrite appsettings.json
            var updatedJson = System.Text.Json.JsonSerializer.Serialize(updatedSettings, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            System.IO.File.WriteAllText(_userSettingsPath, updatedJson);
            ReloadConfiguration();

            return Ok("Settings updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating appsettings.json");
            return StatusCode(500, "Error updating appsettings.json");
        }
    }    

    [HttpDelete]
    [Route("DeleteSettings")]
    public IActionResult DeleteSettings()
    {
        _logger.LogDebug($"{nameof(DeleteSettings)}");

        try
        {
            if (System.IO.File.Exists(_userSettingsPath))
            {
                System.IO.File.Delete(_userSettingsPath);
                ReloadConfiguration();

                return Ok("User settings deleted successfully");
            }
            else
            {
                return NotFound("User settings file not found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user settings");
            return StatusCode(500, "Error deleting user settings");
        }
    }

    private JObject NavigateToProperty(JObject schemaJson, params string[] pathSegments)
    {
        JToken current = schemaJson;

        foreach (var segment in pathSegments)
        {
            current = current?[segment];
            if (current == null)
            {
                return null;
            }
        }

        return current as JObject;
    }

    private void ReloadConfiguration()
    {
        var reloadableConfiguration = (IConfigurationRoot)_configuration;
        reloadableConfiguration.Reload();  // Reloads the configuration from its sources
    }
}



