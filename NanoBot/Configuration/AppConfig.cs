using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace NanoBot.Configuration;

public class AppConfig
{
    public bool ConsoleDebugMode { get; set; }

    [Required]
    [DisplayName("OpenAI API Key")]
    [Description("OpenAI API access key.")]    
    public string OpenAiApiKey { get; set; }

    [Required]
    [DisplayName("OpenAI Model")]
    [Description("OpenAI model to use. Defaults to: 'gpt-4o-mini-realtime-preview'.")]    
    public string OpenAiModel { get; set; } = "gpt-4o-mini-realtime-preview";

    [DisplayName("Global Instructions (modifying this may break correct system functionality)")]
    [Description("Global system instructions for all agents.")]
    public string Instructions { get; set; }

    [DisplayName("Session Timeout Minutes")]
    [Description("Session timeout in minutes. When the session is idle for this number of minutes, it will be automatically closed. Defaults to: '60'.")]
    [DefaultValue(60)]
    public int SessionTimeoutMinutes { get; set; } = 60;

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
    [Description("Enable device driver for Anker PowerConf S330 speakerphone.")]
    public bool S330Enabled { get; set; }

    public List<AgentConfig> Agents { get; set; } = [ ];

    internal readonly string[] OpenAiVoiceNames = [ "alloy", "ash", "ballad", "coral", "echo", "fable", "nova", "onyx", "sage", "shimmer", "verse", "marin", "cedar" ];
    internal readonly string[] OpenAiModels = [ "gpt-realtime", "gpt-realtime-mini", "gpt-4o-realtime-preview", "gpt-4o-mini-realtime-preview"]; // Also see: https://platform.openai.com/docs/pricing
}



