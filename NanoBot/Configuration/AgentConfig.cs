using System.ComponentModel.DataAnnotations;
using System.ComponentModel;

namespace NanoBot.Configuration;

public class AgentConfig
{
    public const string DefaultWakeWord = "alexa_v0.1";

    [Required]
    [Description("Agent name.")]
    public string Name { get; set; }

    [Description("Agent disabled.")]
    public bool Disabled { get; set; }

    [Description("Additional instructions/prompt for the agent. Optional.")]
    public string Instructions { get; set; }

    [Description("Temperature controls the randomness of the completion. The higher the temperature, the more random the completion. Optional.")]
    public double? Temperature { get; set; }

    [Description("The maximum number of tokens to generate in the completion.")]
    public int? MaxTokens { get; set; }

    [Description("TopP controls the diversity of the completion. The higher the TopP, the more diverse the completion. Optional.")]
    public double? TopP { get; set; }

    [Description("Max history size (total messages). Defaults to: '100'.")]
    [DefaultValue(100)]
    public int MaxHistory { get; set; } = 100;

    [Required]
    [Description("User word that wakes up the agent. This is not arbitrary. It needs to be one of the pre-installed wake word models.")]
    [DefaultValue(DefaultWakeWord)]
    public string WakeWord { get; set; } = DefaultWakeWord;

    [Description("Wake word trigger threshold (0.1 - 0.9). Lower number increases sensitivity, higher number reduces false detections. Defaults to: '0.7'.")]
    [Range(0, 10, ErrorMessage = $"{nameof(WakeWordThreshold)} must be a float value between '0.1' and '0.9'.")]
    [DefaultValue(0.7f)]
    public float WakeWordThreshold { get; set; } = 0.7f;
    
    [Description("Wake word trigger level (1 - 10). Lower number increases sensitivity, higher number reduces false detections. Defaults to: '4'.")]
    [Range(1, 10, ErrorMessage = $"{nameof(WakeWordTriggerLevel)} must be an integer value between '1' and '10'. Defaults to: '3'.")]
    [DefaultValue(3)]
    public int WakeWordTriggerLevel { get; set; } = 3;

    //[Required]
    //[Description($"User word that triggers stop of an ongoing conversation. Defaults to: '{DefaultStopWord}'.")]
    //[DefaultValue(DefaultStopWord)]
    //public string StopWord { get; set; } = DefaultStopWord;

    [Required]
    [Description($"Speech synthesis voice name. E.g. 'en-GB-SoniaNeural' if using Azure, 'nova' if using OpenAI.")]
    public string SpeechSynthesisVoiceName { get; set; }

    [DisplayName("Calculator Plug-in")]
    [Description("Provides a set of calculator functions for accurate mathematical operations.")]
    public bool CalculatorPluginEnabled { get; set; }

    [DisplayName("Google Plug-in")]
    [Description("Provides methods to search using Google Custom Search API.")]
    public bool GooglePluginEnabled { get; set; }

    //[DisplayName("User Plug-ins")]
    //[Description("Enable YAML user plug-ins under the 'Plugins/Yaml' folder.")]
    //public bool UserPluginsEnabled { get; set; }

    [DisplayName("DateTime Plug-in")]
    [Description("Provides functions for date and time operations, formatting, and calculations.")]
    public bool DateTimePluginEnabled { get; set; }

    [DisplayName("GeoIP Plug-in")]
    [Description("Provides functions to retrieve current location details based on IP address.")]
    public bool GeoIpPluginEnabled { get; set; }

    [DisplayName("Weather Plug-in")]
    [Description("Provides functions to retrieve weather information and forecasts for specific coordinates.")]
    public bool WeatherPluginEnabled { get; set; }

    [DisplayName("Memory Plug-in")]
    [Description("Provides functions to save, retrieve, search, and manage persistent memories for the agent.")]
    public bool MemoryPluginEnabled { get; set; }
}