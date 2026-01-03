using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace NanoBot.Configuration;

public enum TextToSpeechServiceProviderConfig { AzureSpeechService, OpenAI }

public class AppConfig
{   
    public bool ConsoleDebug { get; set; }  = false;

    [DisplayName("OpenAI Model")]
    [Description("OpenAI model id (e.g. 'gpt-5-mini').")]
    [DefaultValue("gpt-5-mini")]
    public string OpenAiModelId { get; set; } = "gpt-5-mini";

    [DisplayName("OpenAI API Key")]
    [Description("OpenAI API access key.")]
    public string OpenAiApiKey { get; set; }

    [DisplayName("Google API Key")]
    [Description("Google API key used by the Google plug-in.")]
    public string GoogleApiKey { get; set; }

    [DisplayName("Google Search Engine ID")]
    [Description("Google Search Engine Id used by the Google plug-in.")]
    public string GoogleSearchEngineId { get; set; }

    [Description("The Text-To-Speech engine to use. Can be 'AzureSpeechService' or 'OpenAI'.")]
    [DefaultValue(TextToSpeechServiceProviderConfig.AzureSpeechService)]
    public TextToSpeechServiceProviderConfig TextToSpeechServiceProvider { get; set; } = TextToSpeechServiceProviderConfig.AzureSpeechService;

    [DisplayName("Azure Speech Service Key")]
    [Description("Azure Speech Service access key.")]
    public string AzureSpeechServiceKey { get; set; }

    [DisplayName("Azure Speech Service Region")]
    [Description("Azure Speech Service region. E.g. 'westeurope'.")]
    public string AzureSpeechServiceRegion { get; set; }

    [DisplayName("Global Instructions (modifying this may break correct system functionality)")]
    [Description("Global system instructions for all agents.")]
    public string Instructions { get; set; }

    [DisplayName("Chat History TTL")]
    [Description("Delete any existing chat history if the last conversation was after how many minutes. A value of '0' will not delete. Defaults to: '60'.")]
    [DefaultValue(60)]
    public int ChatHistoryTimeToLiveMinutes { get; set; } = 60;

    [DisplayName("Memory Service Max Memories")]
    [Description("Maximum number of memories to store. When exceeded, least frequently used memories will be evicted. Defaults to: '100'.")]
    [DefaultValue(100)]
    public int MemoryServiceMaxMemories { get; set; } = 100;

    [DisplayName("Playback Volume")]
    [Description("Playback volume level (0-10). Defaults to: '5'.")]
    [DefaultValue(5)]
    public int PlaybackVolume { get; set; } = 5;

    [DisplayName("Wake Word Silence Sample Amplitude Threshold")]
    [Description("Threshold for silence detection in wake word. Defaults to: '1200'.")]
    [DefaultValue(1200)]
    [Range(0, 10000)]
    public int WakeWordSilenceSampleAmplitudeThreshold { get; set; } = 1200;

    [DisplayName("Enable Anker PowerConf S330 Driver")]
    [Description("Enable device driver for connected Anker PowerConf S330 speakerphone.")]
    public bool PowerConfS330DriverEnabled { get; set; }

    public List<AgentConfig> Agents { get; set; } = [ ];
}



