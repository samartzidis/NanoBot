/*
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Plugins.Web;
using Microsoft.SemanticKernel.Plugins.Web.Google;
using NanoBot.Configuration;
using NanoBot.Filters;
using NanoBot.Plugins.Native;
using NanoBot.Util;
using System.Text;

namespace NanoBot.Services;

public interface IAgentFactoryService
{
    Task<ChatCompletionAgent> CreateAgentAsync(
        string agentName,
        Action<IKernelBuilder> configAction = null);

    Task<RealtimeConversationAgent> CreateRealtimeAgentAsync(
        string agentName,
        Action<IKernelBuilder> configAction = null);
}

public class AgentFactoryService : IAgentFactoryService
{
    private readonly ILogger<AgentFactoryService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IDynamicOptions<AppConfig> _appConfigOptions;
    private readonly IServiceProvider _serviceProvider;

    public AgentFactoryService(            
        ILogger<AgentFactoryService> logger,
        ILoggerFactory loggerFactory,
        IDynamicOptions<AppConfig> appConfigOptions,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _appConfigOptions = appConfigOptions;
        _serviceProvider = serviceProvider;
        _loggerFactory = loggerFactory;
    }

    public async Task<ChatCompletionAgent> CreateAgentAsync(
        string agentName,
        Action<IKernelBuilder> configAction = null)
    {
        ArgumentNullException.ThrowIfNull(agentName);
        var appConfig = _appConfigOptions.Value;
        var allAgents = appConfig.Agents;

        var agentConfig = allAgents.FirstOrDefault(t => string.Equals(agentName, t.Name, StringComparison.OrdinalIgnoreCase));
        if (agentConfig == null)
            return null;

        _logger.LogInformation($"Building Agent: '{agentName}'");

        // Create the Kernel
        var kernel = await CreateKernel(agentConfig, kernelBuilder =>
        {
            ConfigureKernelPlugins(appConfig, agentConfig, kernelBuilder);

            // Custom kernel config action
            configAction?.Invoke(kernelBuilder);
        });

        // Create and initialize the ChatCompletionAgent
        var promptExecutionSettings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        if (agentConfig.Temperature.HasValue)
            promptExecutionSettings.Temperature = agentConfig.Temperature.Value;
        if (agentConfig.TopP.HasValue)
            promptExecutionSettings.TopP = agentConfig.TopP.Value;
        if (agentConfig.MaxTokens.HasValue)
            promptExecutionSettings.MaxTokens = agentConfig.MaxTokens.Value;

        var reducer = new ChatHistoryTruncationReducer(targetCount: agentConfig.MaxHistory);

        // Combine global prompt with agent prompt
        var instructionsBuilder = new StringBuilder();
        instructionsBuilder.AppendLine(appConfig.Instructions);
        instructionsBuilder.AppendLine(agentConfig.Instructions);
        var instructions = instructionsBuilder.ToString();

        _logger.LogDebug($"Instructions: {instructions}");          

        var agent = new ChatCompletionAgent
        {
            Name = agentName,
            Instructions = instructions,
            Kernel = kernel,
            Arguments = new KernelArguments(promptExecutionSettings),
            HistoryReducer = reducer
        };

        return agent;
    }

    public async Task<RealtimeConversationAgent> CreateRealtimeAgentAsync(
        string agentName,
        Action<IKernelBuilder> configAction = null)
    {
        ArgumentNullException.ThrowIfNull(agentName);
        var appConfig = _appConfigOptions.Value;
        var allAgents = appConfig.Agents;

        var agentConfig = allAgents.FirstOrDefault(t => string.Equals(agentName, t.Name, StringComparison.OrdinalIgnoreCase));
        if (agentConfig == null)
            return null;

        _logger.LogInformation($"Building Agent: '{agentName}'");

        // Create the Kernel
        var kernel = await CreateKernel(agentConfig, kernelBuilder =>
        {
            ConfigureKernelPlugins(appConfig, agentConfig, kernelBuilder);

            // Custom kernel config action
            configAction?.Invoke(kernelBuilder);
        });

        // Combine global prompt with agent prompt
        var instructionsBuilder = new StringBuilder();
        instructionsBuilder.AppendLine(appConfig.Instructions);
        instructionsBuilder.AppendLine(agentConfig.Instructions);
        var instructions = instructionsBuilder.ToString();

        _logger.LogDebug($"Instructions: {instructions}");

        var options = new RealtimeConversationAgentOptions
        {
            Model = "gpt-4o-mini-realtime-preview",
            Voice = !string.IsNullOrEmpty(agentConfig.SpeechSynthesisVoiceName) ? agentConfig.SpeechSynthesisVoiceName : "marin",
            Instructions = instructions,
            OpenAiApiKey = appConfig.OpenAiApiKey,
            OpenAiEndpoint = null
        };
        
        var agent = new RealtimeConversationAgent(_loggerFactory.CreateLogger<RealtimeConversationAgent>(), kernel, Options.Create(options));

        return agent;
    }

    private void ConfigureKernelPlugins(AppConfig appConfig, AgentConfig agentConfig, IKernelBuilder kernelBuilder)
    {
        // Configure Google plugin
        if (agentConfig.GooglePluginEnabled)
        {
            _logger.LogInformation("Adding Google plugin");

            if (string.IsNullOrEmpty(appConfig.GoogleApiKey) || string.IsNullOrEmpty(appConfig.GoogleSearchEngineId))
            {
                var msg = $"Google plug-in enabled but mandatory **{nameof(AppConfig.GoogleApiKey)}** and/or **{nameof(AppConfig.GoogleSearchEngineId)}** settings " +
                          "are not provided in configuration. Google plugin will be disabled. To stop getting this message either disable the Google plug-in " +
                          "in the Agent's configuration or add the missing settings.";
                _logger.LogWarning(msg);
            }
            else
            {
                var googleConnector = new GoogleConnector(
                    apiKey: appConfig.GoogleApiKey,
                    searchEngineId: appConfig.GoogleSearchEngineId,
                    _loggerFactory);

                kernelBuilder.Plugins.AddFromObject(new WebSearchEnginePlugin(googleConnector), "Google");
            }
        }
        else
        {
            _logger.LogInformation("Google plugin not enabled in configuration.");
        }

        // SystemManager plugin
        _logger.LogInformation($"Adding {nameof(SystemManagerPlugin)}");
        kernelBuilder.Plugins.AddFromType<SystemManagerPlugin>(nameof(SystemManagerPlugin));

        // Memory plugin
        if (agentConfig.MemoryPluginEnabled)
        {
            _logger.LogInformation($"Adding {nameof(MemoryPlugin)}");
            kernelBuilder.Plugins.AddFromType<MemoryPlugin>(nameof(MemoryPlugin));
        }

        // EyesPlugin plugin
        if (PlatformUtil.IsRaspberryPi())
        {
            _logger.LogInformation($"Adding {nameof(EyesPlugin)}");

            kernelBuilder.Plugins.AddFromType<EyesPlugin>(nameof(EyesPlugin));
        }

        // Calculator plugin
        if (agentConfig.CalculatorPluginEnabled)
        {
            _logger.LogInformation($"Adding {nameof(CalculatorPlugin)}");

            kernelBuilder.Plugins.AddFromType<CalculatorPlugin>(nameof(CalculatorPlugin));
        }

        // DateTime plugin
        if (agentConfig.DateTimePluginEnabled)
        {
            _logger.LogInformation($"Adding {nameof(DateTimePlugin)}");
            kernelBuilder.Plugins.AddFromType<DateTimePlugin>(nameof(DateTimePlugin));
        }

        // GeoIP plugin
        if (agentConfig.GeoIpPluginEnabled)
        {
            _logger.LogInformation($"Adding {nameof(GeoIpPlugin)}");
            kernelBuilder.Plugins.AddFromType<GeoIpPlugin>(nameof(GeoIpPlugin));
        }

        // Weather plugin
        if (agentConfig.WeatherPluginEnabled)
        {
            _logger.LogInformation($"Adding {nameof(WeatherPlugin)}");
            kernelBuilder.Plugins.AddFromType<WeatherPlugin>(nameof(WeatherPlugin));
        }
    }

    public async Task<Kernel> CreateKernel(AgentConfig agentConfig, Action<IKernelBuilder> configAction = null)
    {
        var appConfig = _appConfigOptions.Value;

        var kernelBuilder = Kernel.CreateBuilder();

        // Optional further configuration by caller
        configAction?.Invoke(kernelBuilder);

        kernelBuilder.Services.AddLogging();

        // Forward register registrations from the parent container
        kernelBuilder.Services.AddTransient<IConfiguration>(_ => _serviceProvider.GetRequiredService<IConfiguration>());
        kernelBuilder.Services.AddTransient<IOptions<AppConfig>>(_ => _serviceProvider.GetRequiredService<IOptions<AppConfig>>());
        kernelBuilder.Services.AddTransient<ILoggerFactory>(_ => _serviceProvider.GetRequiredService<ILoggerFactory>());
        kernelBuilder.Services.AddTransient<IEventBus>(_ => _serviceProvider.GetRequiredService<IEventBus>());
        kernelBuilder.Services.AddTransient<IVoiceService>(_ => _serviceProvider.GetRequiredService<IVoiceService>());
        kernelBuilder.Services.AddTransient<IGpioDeviceService>(_ => _serviceProvider.GetRequiredService<IGpioDeviceService>());
        kernelBuilder.Services.AddTransient<IMemoryService>(_ => _serviceProvider.GetRequiredService<IMemoryService>());
        kernelBuilder.Services.AddTransient<IAlsaControllerService>(_ => _serviceProvider.GetRequiredService<IAlsaControllerService>());

        // Register filters
        kernelBuilder.Services.AddSingleton<IFunctionInvocationFilter, FunctionInvocationLoggingFilter>();
        
        kernelBuilder.Services.ConfigureHttpClientDefaults(c =>
        {

        });        

        // If IChatCompletionService has not already been registered externally
        if (kernelBuilder.Services.All(descriptor => descriptor.ServiceType != typeof(IChatCompletionService)))
        {
            _logger.LogInformation($"Adding OpenAI Chat Completion: {appConfig.OpenAiModelId}");

            // Add Azure OpenAI Chat Completion
            kernelBuilder.AddOpenAIChatCompletion(
                apiKey: appConfig.OpenAiApiKey,
                modelId: appConfig.OpenAiModelId);
        }
        
        var kernel = kernelBuilder.Build();

        return kernel;
    }
}
*/