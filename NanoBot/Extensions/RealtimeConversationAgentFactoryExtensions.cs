using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using NanoBot.Configuration;
using NanoBot.Plugins.Native;
using NanoBot.Util;
using System.Text;

namespace NanoBot.Extensions;

public static class RealtimeConversationAgentFactoryExtensions
{
    public const string DefaultOpenAiModel = "gpt-4o-mini-realtime-preview";
    public const string DefaultSpeechSynthesisVoiceName = "marin";

    public static IServiceCollection AddRealtimeConversationAgentFactory(this IServiceCollection services)
    {
        // Register a base kernel builder configuration
        services.AddSingleton<Func<AgentConfig, RealtimeAgent>>(sp =>
        {
            var appConfig = sp.GetRequiredService<IOptions<AppConfig>>().Value;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            return (agentConfig) =>
            {                
                // Combine global prompt with agent prompt
                var instructionsBuilder = new StringBuilder();
                instructionsBuilder.AppendLine(appConfig.Instructions);
                instructionsBuilder.AppendLine(agentConfig.Instructions);

                // Get instance per dependency kernel instance and configure plugins
                var kernel = sp.GetRequiredService<Kernel>();
                ConfigurePlugins(agentConfig, kernel, loggerFactory, sp, instructionsBuilder);

                var options = new RealtimeAgentOptions
                {
                    Model = !string.IsNullOrEmpty(appConfig.OpenAiModel) ? appConfig.OpenAiModel : DefaultOpenAiModel,
                    Voice = !string.IsNullOrEmpty(agentConfig.SpeechSynthesisVoiceName) ? agentConfig.SpeechSynthesisVoiceName : DefaultSpeechSynthesisVoiceName,
                    Instructions = instructionsBuilder.ToString(),
                    OpenAiApiKey = appConfig.OpenAiApiKey,
                    OpenAiEndpoint = null,
                    Temperature = agentConfig.Temperature
                };

                
                var agent = new RealtimeAgent(sp.GetRequiredService<ILogger<RealtimeAgent>>(),
                    kernel,
                    Options.Create(options));

                return agent;
            };
        });

        return services;
    }

    private static void ConfigurePlugins(AgentConfig agentConfig, Kernel kernel, ILoggerFactory loggerFactory, IServiceProvider sp, StringBuilder instructionsBuilder)
    {
        var logger = loggerFactory.CreateLogger<Program>();

        // SystemManager plugin
        logger.LogInformation($"Adding {nameof(SystemManagerPlugin)}");
        kernel.Plugins.AddFromType<SystemManagerPlugin>(nameof(SystemManagerPlugin), serviceProvider: sp);

        // Memory plugin
        if (agentConfig.MemoryPluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(MemoryPlugin)}");

            instructionsBuilder.AppendLine();
            instructionsBuilder.AppendLine("ALWAYS Use the MemoryPlugin to retrieve relevant memories when needed to answer user questions BEFORE using any other plugin.");
            instructionsBuilder.AppendLine("ALWAYS ask the user before creating a new MemoryPlugin plugin memory or updating an existing one.NEVER do this before asking first.");
            
            kernel.Plugins.AddFromType<MemoryPlugin>(nameof(MemoryPlugin), serviceProvider: sp);
        }

        // EyesPlugin plugin
        if (PlatformUtil.IsRaspberryPi())
        {
            logger.LogInformation($"Adding {nameof(EyesPlugin)}");
            kernel.Plugins.AddFromType<EyesPlugin>(nameof(EyesPlugin), serviceProvider: sp);
        }

        // Calculator plugin
        if (agentConfig.CalculatorPluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(CalculatorPlugin)}");

            instructionsBuilder.AppendLine();
            instructionsBuilder.AppendLine($"ALWAYS Use the {nameof(CalculatorPlugin)} if you need assistance in mathematical operations.");

            kernel.Plugins.AddFromType<CalculatorPlugin>(nameof(CalculatorPlugin), serviceProvider: sp);
        }

        // DateTime plugin
        if (agentConfig.DateTimePluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(DateTimePlugin)}");

            instructionsBuilder.AppendLine();
            instructionsBuilder.AppendLine($"ALWAYS Use the {nameof(DateTimePlugin)} for date and time information.");

            kernel.Plugins.AddFromType<DateTimePlugin>(nameof(DateTimePlugin), serviceProvider: sp);
        }

        // GeoIP plugin
        if (agentConfig.GeoIpPluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(GeoIpPlugin)}");

            instructionsBuilder.AppendLine();
            instructionsBuilder.AppendLine($"ALWAYS Use the {nameof(GeoIpPlugin)} for date and time information.");

            kernel.Plugins.AddFromType<GeoIpPlugin>(nameof(GeoIpPlugin), serviceProvider: sp);
        }

        // Weather plugin
        if (agentConfig.WeatherPluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(WeatherPlugin)}");

            instructionsBuilder.AppendLine();
            instructionsBuilder.AppendLine($"ALWAYS Use the {nameof(WeatherPlugin)} for weather information.");

            kernel.Plugins.AddFromType<WeatherPlugin>(nameof(WeatherPlugin), serviceProvider: sp);
        }
    }
}