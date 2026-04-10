using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace NanoBot.Configuration;

public class AppConfig
{
    private const string DefaultOpenAiModel = "gpt-realtime-mini";
    private const string DefaultPowerOpenAiModel = "gpt-5.4";

    public bool ConsoleDebugMode { get; set; }

    [DisplayName("File Logging")]
    [Description("Enable file logging in the application directory. Defaults to: 'false'.")]
    [DefaultValue(false)]
    public bool FileLoggingEnabled { get; set; }

    [DisplayName("Night Mode")]
    [Description("Enable night mode. In this mode, eyes will automatically turn off when idle for more than the configured timeout. Defaults to: 'false'.")]
    [DefaultValue(false)]
    public bool NightModeEnabled { get; set; }

    [DisplayName("Night Mode Idle Timeout Minutes")]
    [Description("Minutes of inactivity before night mode turns eyes off. Only applies when night mode is enabled. Defaults to: '10'.")]
    [DefaultValue(10)]
    [Range(1, 600)]
    public int NightModeIdleTimeoutMinutes { get; set; } = 10;

    [Required]
    [DisplayName("OpenAI API Key")]
    [Description("OpenAI API access key.")]    
    public string OpenAiApiKey { get; set; }

    [Required]
    [DisplayName("AI Model")]
    [Description("OpenAI model to use.")]
    [DefaultValue(DefaultOpenAiModel)]
    public string OpenAiModel { get; set; } = DefaultOpenAiModel;

    [DisplayName("Global Instructions (modifying this may break correct system functionality)")]
    [Description("Global system instructions for all agents.")]
    public string Instructions { get; set; }

    [DisplayName("Session Timeout Minutes")]
    [Description("Session timeout in minutes. When the session is idle for this number of minutes, it will be automatically closed. Defaults to: '30'.")]
    [DefaultValue(30)]
    [Range(1, 60)]
    public int SessionTimeoutMinutes { get; set; } = 30;

    [DisplayName("Conversation Inactivity Timeout Seconds")]
    [Description("Conversation inactivity timeout in seconds. When the conversation is inactive for this number of seconds, it will be automatically go back to Wake Word detection mode. Defaults to: '10'.")]
    [DefaultValue(10)]
    [Range(1, 60)]
    public int ConversationInactivityTimeoutSeconds { get; set; } = 10;

    [DisplayName("Memory Service Max Memories")]
    [Description("Maximum number of memories to store. When exceeded, least frequently used memories will be evicted. Defaults to: '100'.")]
    [DefaultValue(100)]
    [Range(10, 1000)]
    public int MemoryServiceMaxMemories { get; set; } = 100;

    [DisplayName("Startup Playback Volume")]
    [Description("Startup playback volume level (0-10). If unspecified the system will not set the volume level at startup.")]
    [DefaultValue(null)]
    [Range(0, 10)]
    public int? PlaybackVolume { get; set; }

    [DisplayName("Wake Word Silence Sample Amplitude Threshold")]
    [Description("Threshold for silence detection in wake word. Defaults to: '800'.")]
    [DefaultValue(800)]
    [Range(0, 10000)]
    public int WakeWordSilenceSampleAmplitudeThreshold { get; set; } = 800;

    [DisplayName("Anker PowerConf S330 Driver")]
    [Description("Enable device driver for Anker PowerConf S330 speakerphone.")]
    public bool S330Enabled { get; set; }

    [Required]
    [DisplayName("Power AI Model")]
    [Description("Chat completion model used by the Power AI plug-in.")]
    [DefaultValue(DefaultPowerOpenAiModel)]
    public string PowerOpenAiModel { get; set; } = DefaultPowerOpenAiModel;

    public List<AgentConfig> Agents { get; set; } = [ ];

    internal readonly string[] OpenAiVoiceNames = [ "alloy", "ash", "ballad", "coral", "echo", "fable", "nova", "onyx", "sage", "shimmer", "verse", "marin", "cedar" ];
    internal readonly string[] OpenAiModels = [DefaultOpenAiModel, "gpt-realtime", "gpt-realtime-1.5"]; // Also see: https://platform.openai.com/docs/pricing
    internal readonly string[] PowerOpenAiModels = [DefaultPowerOpenAiModel, "gpt-5.4-mini", "gpt-5.4-nano" ];
}



