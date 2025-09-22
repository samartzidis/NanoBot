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

public static class OpenAiModelIdConfigExtensions
{
    public static string ToModelString(this OpenAiModelIdConfig model)
    {
        return model switch
        {
            OpenAiModelIdConfig.Gpt35Turbo => "gpt-3.5-turbo",
            OpenAiModelIdConfig.Gpt4oMini => "gpt-4o-mini",
            OpenAiModelIdConfig.Gpt4o => "gpt-4o",
            OpenAiModelIdConfig.Gpt5Mini => "gpt-5-mini",
            OpenAiModelIdConfig.Gpt5Nano => "gpt-5-nano",
            OpenAiModelIdConfig.Gpt5 => "gpt-5",
            _ => "gpt-4o-mini"
        };
    }
}

public class AppConfig
{
    [Required]
    [DisplayName("OpenAI Model")]
    [Description("OpenAI model.")]
    [DefaultValue(Configuration.OpenAiModelIdConfig.Gpt4oMini)]
    public OpenAiModelIdConfig OpenAiModelId { get; set; } = OpenAiModelIdConfig.Gpt4oMini;

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



