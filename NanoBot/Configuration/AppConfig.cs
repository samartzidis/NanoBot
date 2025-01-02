using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace NanoBot.Configuration;

public enum TextToSpeechServiceProviderConfig { AzureSpeechService, OpenAI }

public class AppConfig
{
    public const string DefaultOpenAiModelId = "gpt-4o-mini";

    [Required]
    [DisplayName("OpenAI Model")]
    [Description($"Azure OpenAI model id. Defaults to: {DefaultOpenAiModelId}")]
    [DefaultValue(DefaultOpenAiModelId)]
    public string OpenAiModelId { get; set; } = DefaultOpenAiModelId;

    [DisplayName("OpenAI API Key")]
    [Description("OpenAI API access key.")]
    public string OpenAiApiKey { get; set; }

    [DisplayName("Google API Key")]
    [Description("Google API key used by the Google plug-in.")]
    public string GoogleApiKey { get; set; }

    [DisplayName("Google Search Engine ID")]
    [Description("Google Search Engine Id used by the Google plug-in.")]
    public string GoogleSearchEngineId { get; set; }

    [DisplayName("Enable Anker PowerConf S330 Driver")]
    [Description("Enable device driver for connected Anker PowerConf S330 speakerphone.")]
    public bool PowerConfS330DriverEnabled { get; set; }

    [DisplayName("Path of 'mplayer' Executable")]
    [Description("Override the default 'mplayer' (or 'mplayer.exe') executable path. Leave blank if included in the system path.")]
    public string MPlayerPath { get; set; }

    public VoiceServiceConfig VoiceService { get; set; } = new();

    [DisplayName("Global Instructions (modifying this may break correct system functionality)")]
    [Description("Global system instructions for all agents.")]
    public string Instructions { get; set; }

    public List<AgentConfig> Agents { get; set; } = [ ];
}

public class VoiceServiceConfig
{    
    [Description("Calibration parameter for silence detection.")]
    [Range(10, 2000, ErrorMessage = $"{nameof(SilenceSampleAmplitudeThreshold)} must be between '10' and '2000'. Defaults to: '800'.")]
    [DefaultValue(800)]
    public int SilenceSampleAmplitudeThreshold { get; set; } = 800;

    [Description("Calibration parameter for silecne detection.")]
    [Range(5, 100, ErrorMessage = $"{nameof(SilenceSampleCountThreshold)} must be between '5' and '100'. Defaults to: '50'.")]
    [DefaultValue(50)]
    public int SilenceSampleCountThreshold { get; set; } = 50;

    [Description("User voice message max recording duration if no silence is detected.")]
    [Range(5, 60, ErrorMessage = $"{nameof(MaxRecordingDurationSeconds)} must be between '5' and '60'. Defaults to: '30'.")]
    [DefaultValue(30)]
    public int MaxRecordingDurationSeconds { get; set; } = 30;

    [Description("Silence duration to mark end of recording of user voice message.")]
    [Range(1, 60, ErrorMessage = $"{nameof(StopRecordingSilenceSeconds)} must be between '1' and '10'. Defaults to: '5'.")]
    [DefaultValue(5)]
    public int StopRecordingSilenceSeconds { get; set; } = 5;

    [Description("The Text-To-Speech engine to use. Can be 'AzureSpeechService' or 'OpenAI'.")]
    [DefaultValue(TextToSpeechServiceProviderConfig.AzureSpeechService)]
    public TextToSpeechServiceProviderConfig TextToSpeechServiceProvider { get; set; } = TextToSpeechServiceProviderConfig.AzureSpeechService;

    [DisplayName("Azure Speech Service Key")]
    [Description("Azure Speech Service access key.")]
    public string AzureSpeechServiceKey { get; set; }

    [DisplayName("Azure Speech Service Region")]
    [Description("Azure Speech Service region. E.g. 'westeurope'.")]
    public string AzureSpeechServiceRegion { get; set; }
}

public class AgentConfig
{
    public const string DefaultWakeWord = "alexa_v0.1";
    public const string DefaultStopWord = "stop";
    public const string DefaultSpeechSynthesisVoiceName = "en-GB-SoniaNeural";

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
    [Description($"User word that triggers stop of an ongoing conversation. Defaults to: '{nameof(DefaultStopWord)}'.")]
    [DefaultValue(DefaultStopWord)]
    public string StopWord { get; set; } = DefaultStopWord;

    [Required]
    [Description($"Speech synthesis voice name. E.g. 'en-GB-SoniaNeural' if using Azure, 'nova' if using OpenAI. Defaults to: '{nameof(DefaultSpeechSynthesisVoiceName)}'.")]
    [DefaultValue(DefaultSpeechSynthesisVoiceName)]
    public string SpeechSynthesisVoiceName { get; set; } = DefaultSpeechSynthesisVoiceName;

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

