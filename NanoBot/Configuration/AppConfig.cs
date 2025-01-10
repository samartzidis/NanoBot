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

    [DisplayName("Chat History TTL")]
    [Description("Delete any existing chat history if the last conversation was after how many minutes. A value of '0' will not delete. Defaults to: '60'.")]
    [DefaultValue(60)]
    public int ChatHistoryTimeToLiveMinutes { get; set; } = 60;

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



