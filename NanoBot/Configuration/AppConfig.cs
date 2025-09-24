using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace NanoBot.Configuration;

public enum TextToSpeechServiceProviderConfig { AzureSpeechService, OpenAI }

public enum OpenAiModelIdConfig
{
    Gpt35Turbo,
    Gpt4oMini,
    Gpt4o,
    Gpt5Mini,
    Gpt5Nano,
    Gpt5
}

public class AppConfig
{   
    public bool ConsoleDebug { get; set; }  = false;

    [Required]
    [DisplayName("OpenAI Model")]
    [Description("OpenAI model id (e.g. 'gpt-4o-mini').")]
    [DefaultValue("gpt-4o-mini")]
    public string OpenAiModelId { get; set; } = "gpt-4o-mini";

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



