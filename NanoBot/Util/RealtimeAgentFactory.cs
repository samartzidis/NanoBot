using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NanoBot.Configuration;
using NanoBot.Plugins;
using NanoBot.Services;
using System.ComponentModel;
using System.Reflection;
using System.Text;

namespace NanoBot.Util;

public sealed class RealtimeAgentFactory : IRealtimeAgentFactory
{
    private readonly IOptionsMonitor<AppConfig> _appConfigMonitor;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly IEventBus _bus;

    public RealtimeAgentFactory(
        IOptionsMonitor<AppConfig> appConfigMonitor,
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider,
        IEventBus bus)
    {
        _appConfigMonitor = appConfigMonitor;
        _loggerFactory = loggerFactory;
        _serviceProvider = serviceProvider;
        _bus = bus;
    }

    public RealtimeAgent Create(AgentConfig agentConfig)
    {
        var appConfig = _appConfigMonitor.CurrentValue;

        var instructionsBuilder = new StringBuilder();
        instructionsBuilder.AppendLine(appConfig.Instructions);
        instructionsBuilder.AppendLine(agentConfig.Instructions);

        var tools = ConfigureTools(agentConfig, appConfig, instructionsBuilder);

        var options = new RealtimeAgentOptions
        {
            Model = appConfig.OpenAiModel,
            Voice = agentConfig.SpeechSynthesisVoiceName,
            Instructions = instructionsBuilder.ToString(),
            OpenAiApiKey = appConfig.OpenAiApiKey,
            OpenAiEndpoint = null,
            Temperature = agentConfig.Temperature,
            SpeechSpeed = agentConfig.SpeechSpeed,
            ConversationInactivityTimeoutSeconds = appConfig.ConversationInactivityTimeoutSeconds,
        };

        return new RealtimeAgent(
            _loggerFactory.CreateLogger<RealtimeAgent>(),
            tools,
            _bus,
            Options.Create(options));
    }

    private List<AIFunction> ConfigureTools(AgentConfig agentConfig, AppConfig appConfig, StringBuilder instructionsBuilder)
    {
        var logger = _loggerFactory.CreateLogger<Program>();
        var tools = new List<AIFunction>();

        logger.LogInformation($"Adding {nameof(SystemManagerPlugin)}");
        tools.AddRange(CreateToolsFromType<SystemManagerPlugin>(nameof(SystemManagerPlugin)));

        if (agentConfig.MemoryPluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(MemoryPlugin)}");

            instructionsBuilder.AppendLine();
            instructionsBuilder.AppendLine("ALWAYS Use the MemoryPlugin to retrieve relevant memories when needed to answer user questions BEFORE using any other plugin.");
            instructionsBuilder.AppendLine("ALWAYS ask the user before creating a new MemoryPlugin plugin memory or updating an existing one.NEVER do this before asking first.");

            tools.AddRange(CreateToolsFromType<MemoryPlugin>(nameof(MemoryPlugin)));
        }

        if (PlatformUtil.IsRaspberryPi())
        {
            logger.LogInformation($"Adding {nameof(EyesPlugin)}");
            tools.AddRange(CreateToolsFromType<EyesPlugin>(nameof(EyesPlugin)));
        }

        if (agentConfig.CalculatorPluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(CalculatorPlugin)}");

            instructionsBuilder.AppendLine();
            instructionsBuilder.AppendLine($"ALWAYS use the {nameof(CalculatorPlugin)} if you need assistance in mathematical operations.");

            tools.AddRange(CreateToolsFromType<CalculatorPlugin>(nameof(CalculatorPlugin)));
        }

        if (agentConfig.GeoIpPluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(GeoIpPlugin)}");

            instructionsBuilder.AppendLine();
            instructionsBuilder.AppendLine($"ALWAYS use the {nameof(GeoIpPlugin)} for date and time information.");

            tools.AddRange(CreateToolsFromType<GeoIpPlugin>(nameof(GeoIpPlugin)));
        }

        if (agentConfig.DateTimePluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(DateTimePlugin)}");

            instructionsBuilder.AppendLine();
            instructionsBuilder.AppendLine($"ALWAYS use the {nameof(DateTimePlugin)} when the user asks for the current date or time. Defaults to the system's local time zone.");

            tools.AddRange(CreateToolsFromType<DateTimePlugin>(nameof(DateTimePlugin)));
        }

        if (agentConfig.WeatherPluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(WeatherPlugin)}");

            instructionsBuilder.AppendLine();
            instructionsBuilder.AppendLine($"ALWAYS Use the {nameof(WeatherPlugin)} for weather information.");

            tools.AddRange(CreateToolsFromType<WeatherPlugin>(nameof(WeatherPlugin)));
        }

        if (agentConfig.PowerAIPluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(PowerAIPlugin)} with model '{appConfig.PowerOpenAiModel}'");

            if (!string.IsNullOrWhiteSpace(appConfig.PowerAIPluginInstructions))
            {
                instructionsBuilder.AppendLine();
                instructionsBuilder.AppendLine(appConfig.PowerAIPluginInstructions);
            }

            var powerAiPlugin = new PowerAIPlugin(
                _loggerFactory.CreateLogger<PowerAIPlugin>(),
                appConfig.OpenAiApiKey,
                appConfig.PowerOpenAiModel,
                tools.ToList());
            tools.AddRange(CreateToolsFromObject(powerAiPlugin, nameof(PowerAIPlugin)));
        }

        return tools;
    }

    private List<AIFunction> CreateToolsFromType<T>(string pluginName) where T : class
    {
        var instance = ActivatorUtilities.CreateInstance<T>(_serviceProvider);
        return CreateToolsFromObject(instance, pluginName);
    }

    private static List<AIFunction> CreateToolsFromObject(object target, string pluginName)
    {
        return target.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<DescriptionAttribute>() != null)
            .Select(m => AIFunctionFactory.Create(m, target, new AIFunctionFactoryOptions
            {
                Name = $"{pluginName}-{m.Name}"
            }))
            .ToList();
    }
}
