using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NanoBot.Configuration;
using NanoBot.Events;
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
    //private readonly IVoiceService _voiceService;
    private readonly IConfiguration _configuration;
    private readonly IEventBus _bus;

    public ConfigurationController(
        IDynamicOptions<AppConfig> appConfigOptions, 
        ILogger<ConfigurationController> logger, 
        //IVoiceService voiceService,
        IConfiguration configuration,
        IEventBus bus)
    {
        _appConfigOptions = appConfigOptions;
        _logger = logger;
        //_voiceService = voiceService;
        _userSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.user.json");
        _configuration = configuration;
        _bus = bus;
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

            var agentsConfig = NavigateToProperty(schemaJson, "properties", "Agents", "items", "properties");

            // Fetch available voices
            //var voiceProvider = _appConfigOptions.Value.TextToSpeechServiceProvider;
            //var voices = await _voiceService.GetAvailableVoicesAsync(voiceProvider, cancellationToken);
            //if (voices != null)
            //{
            //    var voiceNames = voices.Select(t => t.ShortName).ToArray();

            //    // Update schema for "SpeechSynthesisVoiceName"              
            //    if (agentsConfig != null && agentsConfig["SpeechSynthesisVoiceName"] != null)
            //        agentsConfig["SpeechSynthesisVoiceName"]["enum"] = JArray.FromObject(voiceNames);
            //}

            // Update schema for "SpeechSynthesisVoiceName"
            var voiceNames = _appConfigOptions.Value.OpenAiVoiceNames;
            if (agentsConfig != null && agentsConfig["SpeechSynthesisVoiceName"] != null)
                agentsConfig["SpeechSynthesisVoiceName"]["enum"] = JArray.FromObject(voiceNames);

            // Fetch available wake words
            var wakeWordModels = WakeWordUtil.GetModels();
            if (wakeWordModels != null)
            {
                // Update schema for "WakeWord"
                if (agentsConfig != null && agentsConfig["WakeWord"] != null)
                    agentsConfig["WakeWord"]["enum"] = JArray.FromObject(wakeWordModels);
            }

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

            // Clear out "Instructions"
            appConfig.Instructions = null;

            var settings = new JsonSerializerSettings {
                Formatting = Formatting.Indented
            };

            settings.Converters.Add(new StringEnumConverter());

            var json = JsonConvert.SerializeObject(appConfig, settings);

            return Content(json, "application/json");            
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading settings.");

            return StatusCode(500, "Error reading settings.");
        }
    }

    [HttpPost]
    [Route("UpdateSettings")]
    public IActionResult UpdateSettings([FromBody] AppConfig appConfig)
    {
        _logger.LogDebug($"{nameof(UpdateSettings)}");

        try
        {
            // Clear out "Instructions"
            appConfig.Instructions = null;

            // Serialize the updated settings and write appsettings.user.json
            var updatedJson = System.Text.Json.JsonSerializer.Serialize(appConfig, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            System.IO.File.WriteAllText(_userSettingsPath, updatedJson);
            ReloadConfiguration();

            // Publish config changed event to notify services
            _bus.Publish<ConfigChangedEvent>(this);

            return Ok("Settings updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating {_userSettingsPath}.");

            return StatusCode(500, "Error updating user settings.");
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

                // Publish config changed event to notify services
                _bus.Publish<ConfigChangedEvent>(this);
            }

            return Ok("Custom user settings cleared successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error deleting {_userSettingsPath}.");

            return StatusCode(500, "Error deleting user settings.");
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



