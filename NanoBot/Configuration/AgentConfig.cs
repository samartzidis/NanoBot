using System.ComponentModel.DataAnnotations;
using System.ComponentModel;

namespace NanoBot.Configuration;

public class AgentConfig
{
    public const string DefaultWakeWord = "alexa_v0.1";
    public const string DefaultStopWord = "stop";

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

    [Description("Max history size (total messages). Defaults to: '20'.")]
    [DefaultValue(20)]
    public int MaxHistory { get; set; } = 20;

    [Required]
    [Description("User word that wakes up the agent. This is not arbitrary. It needs to be one of the pre-installed wake word models.")]
    [DefaultValue(DefaultWakeWord)]
    public string WakeWord { get; set; } = DefaultWakeWord;

    [Description("Wake word threshold. Defaults to: '0.5'.")]
    [Range(0, 1, ErrorMessage = $"{nameof(WakeWordThreshold)} must be a float value between '0' and '1'. Defaults to: '0.5'.")]
    [DefaultValue(0.5f)]
    public float WakeWordThreshold { get; set; } = 0.5f;

    [Description("Wake word trigger level. Defaults to: '4'.")]
    [Range(1, 10, ErrorMessage = $"{nameof(WakeWordTriggerLevel)} must be an integer value between '1' and '10'. Defaults to: '4'.")]
    [DefaultValue(4)]
    public int WakeWordTriggerLevel { get; set; } = 4;

    [Required]
    [Description($"User word that triggers stop of an ongoing conversation. Defaults to: '{DefaultStopWord}'.")]
    [DefaultValue(DefaultStopWord)]
    public string StopWord { get; set; } = DefaultStopWord;

    [Required]
    [Description($"Speech synthesis voice name. E.g. 'en-GB-SoniaNeural' if using Azure, 'nova' if using OpenAI.")]
    public string SpeechSynthesisVoiceName { get; set; }

    [DisplayName("Time Plug-in")]
    [Description("Provides a set of functions to get the current time and date.")]
    public bool TimePluginEnabled { get; set; }

    [DisplayName("Google Plug-in")]
    [Description("Provides methods to search using Google Custom Search API.")]
    public bool GooglePluginEnabled { get; set; }

    [DisplayName("YouTube Plug-in")]
    [Description("Provides access to YouTube videos for playing audio streams.")]
    public bool YouTubePluginEnabled { get; set; }

    [DisplayName("Memory Plug-in")]
    [Description("Allows persistent memories.")]
    public bool MemoryPluginEnabled { get; set; }
}