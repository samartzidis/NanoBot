using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NanoBot.Configuration;
using NanoBot.Plugins.Native;
using NanoBot.Services;
using NanoBot.Util;
using System.ComponentModel;
using System.Reflection;
using System.Text;

namespace NanoBot.Extensions;

public static class RealtimeConversationAgentFactoryExtensions
{
    public const string DefaultOpenAiModel = "gpt-4o-mini-realtime-preview";
    public const string DefaultSpeechSynthesisVoiceName = "marin";

    public static IServiceCollection AddRealtimeConversationAgentFactory(this IServiceCollection services)
    {
        services.AddSingleton<Func<AgentConfig, RealtimeAgent>>(sp =>
        {
            var appConfig = sp.GetRequiredService<IOptions<AppConfig>>().Value;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            return (agentConfig) =>
            {                
                var instructionsBuilder = new StringBuilder();
                instructionsBuilder.AppendLine(appConfig.Instructions);
                instructionsBuilder.AppendLine(agentConfig.Instructions);

                var tools = ConfigureTools(agentConfig, loggerFactory, sp, instructionsBuilder);

                var options = new RealtimeAgentOptions
                {
                    Model = !string.IsNullOrEmpty(appConfig.OpenAiModel) ? appConfig.OpenAiModel : DefaultOpenAiModel,
                    Voice = !string.IsNullOrEmpty(agentConfig.SpeechSynthesisVoiceName) ? agentConfig.SpeechSynthesisVoiceName : DefaultSpeechSynthesisVoiceName,
                    Instructions = instructionsBuilder.ToString(),
                    OpenAiApiKey = appConfig.OpenAiApiKey,
                    OpenAiEndpoint = null,
                    Temperature = agentConfig.Temperature,
                    ConversationInactivityTimeoutSeconds = appConfig.ConversationInactivityTimeoutSeconds,
                };

                var agent = new RealtimeAgent(
                    sp.GetRequiredService<ILogger<RealtimeAgent>>(),
                    tools,
                    sp.GetRequiredService<IEventBus>(),
                    Options.Create(options));

                return agent;
            };
        });

        return services;
    }

    private static List<AIFunction> ConfigureTools(AgentConfig agentConfig, ILoggerFactory loggerFactory, IServiceProvider sp, StringBuilder instructionsBuilder)
    {
        var logger = loggerFactory.CreateLogger<Program>();
        var tools = new List<AIFunction>();

        logger.LogInformation($"Adding {nameof(SystemManagerPlugin)}");
        tools.AddRange(CreateToolsFromType<SystemManagerPlugin>(nameof(SystemManagerPlugin), sp));

        if (agentConfig.MemoryPluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(MemoryPlugin)}");

            instructionsBuilder.AppendLine();
            instructionsBuilder.AppendLine("ALWAYS Use the MemoryPlugin to retrieve relevant memories when needed to answer user questions BEFORE using any other plugin.");
            instructionsBuilder.AppendLine("ALWAYS ask the user before creating a new MemoryPlugin plugin memory or updating an existing one.NEVER do this before asking first.");
            
            tools.AddRange(CreateToolsFromType<MemoryPlugin>(nameof(MemoryPlugin), sp));
        }

        if (PlatformUtil.IsRaspberryPi())
        {
            logger.LogInformation($"Adding {nameof(EyesPlugin)}");
            tools.AddRange(CreateToolsFromType<EyesPlugin>(nameof(EyesPlugin), sp));
        }

        if (agentConfig.CalculatorPluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(CalculatorPlugin)}");

            instructionsBuilder.AppendLine();
            instructionsBuilder.AppendLine($"ALWAYS use the {nameof(CalculatorPlugin)} if you need assistance in mathematical operations.");

            tools.AddRange(CreateToolsFromType<CalculatorPlugin>(nameof(CalculatorPlugin), sp));
        }

        if (agentConfig.DateTimePluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(DateTimePlugin)}");

            instructionsBuilder.AppendLine();
            instructionsBuilder.AppendLine($"ALWAYS use the {nameof(DateTimePlugin)} for date and time information.");

            tools.AddRange(CreateToolsFromType<DateTimePlugin>(nameof(DateTimePlugin), sp));
        }

        if (agentConfig.GeoIpPluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(GeoIpPlugin)}");

            instructionsBuilder.AppendLine();
            instructionsBuilder.AppendLine($"ALWAYS use the {nameof(GeoIpPlugin)} for date and time information.");

            tools.AddRange(CreateToolsFromType<GeoIpPlugin>(nameof(GeoIpPlugin), sp));
        }

        if (agentConfig.WeatherPluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(WeatherPlugin)}");

            instructionsBuilder.AppendLine();
            instructionsBuilder.AppendLine($"ALWAYS Use the {nameof(WeatherPlugin)} for weather information.");

            tools.AddRange(CreateToolsFromType<WeatherPlugin>(nameof(WeatherPlugin), sp));
        }

        if (agentConfig.PowerAIPluginEnabled)
        {
            var appConfig = sp.GetRequiredService<IOptions<AppConfig>>().Value;
            logger.LogInformation($"Adding {nameof(PowerAIPlugin)} with model '{appConfig.PowerOpenAiModel}'");

            if (!string.IsNullOrWhiteSpace(appConfig.PowerAIPluginInstructions))
            {
                instructionsBuilder.AppendLine();
                instructionsBuilder.AppendLine(appConfig.PowerAIPluginInstructions);
            }

            var powerAiPlugin = new PowerAIPlugin(
                loggerFactory.CreateLogger<PowerAIPlugin>(),
                appConfig.OpenAiApiKey,
                appConfig.PowerOpenAiModel,
                tools.ToList());
            tools.AddRange(CreateToolsFromObject(powerAiPlugin, nameof(PowerAIPlugin)));
        }

        return tools;
    }

    private static List<AIFunction> CreateToolsFromType<T>(string pluginName, IServiceProvider sp) where T : class
    {
        var instance = ActivatorUtilities.CreateInstance<T>(sp);
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
