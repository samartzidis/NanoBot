using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Web;
using Microsoft.SemanticKernel.Plugins.Web.Google;
using NanoBot.Configuration;
using NanoBot.Plugins.Native;
using NanoBot.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NanoBot;

public static class RealtimeConversationAgentFactoryExtensions
{
    public static IServiceCollection AddRealtimeConversationAgentFactory(this IServiceCollection services)
    {
        // Register a base kernel builder configuration
        services.AddSingleton<Func<AgentConfig, RealtimeConversationAgent>>(sp =>
        {
            var appConfig = sp.GetRequiredService<IOptions<AppConfig>>().Value;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            return (agentConfig) =>
            {
                // Combine global prompt with agent prompt
                var instructionsBuilder = new StringBuilder();
                instructionsBuilder.AppendLine(appConfig.Instructions);
                instructionsBuilder.AppendLine(agentConfig.Instructions);
                var instructions = instructionsBuilder.ToString();

                var options = new RealtimeConversationAgentOptions
                {
                    Model = "gpt-4o-mini-realtime-preview",
                    Voice = "marin",
                    Instructions = instructions,
                    OpenAiApiKey = appConfig.OpenAiApiKey,
                    OpenAiEndpoint = null
                };

                var kernel = sp.GetRequiredService<Kernel>();

                ConfigurePlugins(appConfig, agentConfig, kernel, loggerFactory, sp);

                var agent = new RealtimeConversationAgent(sp.GetRequiredService<ILogger<RealtimeConversationAgent>>(),
                    kernel,
                    Options.Create(options));

                return agent;
            };
        });

        return services;
    }

    private static void ConfigurePlugins(AppConfig appConfig, AgentConfig agentConfig, Kernel kernel, ILoggerFactory loggerFactory, IServiceProvider sp)
    {
        var logger = loggerFactory.CreateLogger<Program>();

        // Configure Google plugin
        if (agentConfig.GooglePluginEnabled)
        {
            //_logger.LogInformation("Adding Google plugin");

            if (string.IsNullOrEmpty(appConfig.GoogleApiKey) || string.IsNullOrEmpty(appConfig.GoogleSearchEngineId))
            {
                var msg = $"Google plug-in enabled but mandatory **{nameof(AppConfig.GoogleApiKey)}** and/or **{nameof(AppConfig.GoogleSearchEngineId)}** settings " +
                          "are not provided in configuration. Google plugin will be disabled. To stop getting this message either disable the Google plug-in " +
                          "in the Agent's configuration or add the missing settings.";
                logger.LogWarning(msg);
            }
            else
            {
                var googleConnector = new GoogleConnector(
                    apiKey: appConfig.GoogleApiKey,
                    searchEngineId: appConfig.GoogleSearchEngineId,
                    loggerFactory);

                kernel.Plugins.AddFromObject(new WebSearchEnginePlugin(googleConnector), "Google");
            }
        }
        else
        {
            logger.LogInformation("Google plugin not enabled in configuration.");
        }

        // SystemManager plugin
        logger.LogInformation($"Adding {nameof(SystemManagerPlugin)}");
        kernel.Plugins.AddFromType<SystemManagerPlugin>(nameof(SystemManagerPlugin), serviceProvider: sp);

        // Memory plugin
        if (agentConfig.MemoryPluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(MemoryPlugin)}");
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
            kernel.Plugins.AddFromType<CalculatorPlugin>(nameof(CalculatorPlugin), serviceProvider: sp);
        }

        // DateTime plugin
        if (agentConfig.DateTimePluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(DateTimePlugin)}");
            kernel.Plugins.AddFromType<DateTimePlugin>(nameof(DateTimePlugin), serviceProvider: sp);
        }

        // GeoIP plugin
        if (agentConfig.GeoIpPluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(GeoIpPlugin)}");
            kernel.Plugins.AddFromType<GeoIpPlugin>(nameof(GeoIpPlugin), serviceProvider: sp);
        }

        // Weather plugin
        if (agentConfig.WeatherPluginEnabled)
        {
            logger.LogInformation($"Adding {nameof(WeatherPlugin)}");
            kernel.Plugins.AddFromType<WeatherPlugin>(nameof(WeatherPlugin), serviceProvider: sp);
        }
    }
}