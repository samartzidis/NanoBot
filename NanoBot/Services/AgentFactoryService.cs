using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Core;
using NanoBot.Configuration;
using Microsoft.SemanticKernel.Plugins.Web;
using Microsoft.SemanticKernel.Plugins.Web.Google;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using NanoBot.Plugins.Native;
using System.Text;
using Microsoft.SemanticKernel.Plugins.Memory;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Connectors.Sqlite;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.SemanticKernel.ChatCompletion;
using Polly;
using NanoBot.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NanoBot.Util;

namespace NanoBot.Services;

public interface IAgentFactoryService
{
    Task<ChatCompletionAgent> CreateAgentAsync(
        string agentName,
        Action<IKernelBuilder> configAction = null,
        CancellationToken cancellationToken = default);        
}

public class AgentFactoryService : IAgentFactoryService
{
    private const string MemoryPluginDataFolder = "MemoryPluginData";
    private const string TextEmbeddingModelId = "text-embedding-ada-002";

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
        Action<IKernelBuilder> configAction = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentName);
        var appConfig = _appConfigOptions.Value;
        var allAgents = appConfig.Agents;

        var agentConfig = allAgents.FirstOrDefault(t => string.Equals(agentName, t.Name, StringComparison.OrdinalIgnoreCase));
        if (agentConfig == null)
            return null;

        _logger.LogInformation($"Building Agent: '{agentName}'");

        // Create the Kernel
        var kernel = await CreateKernel(kernelBuilder =>
        {
            // Configure Google plugin
            if (agentConfig.GooglePluginEnabled)
            {
                _logger.LogDebug("Adding GooglePlugin");

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

                    kernelBuilder.Plugins.AddFromObject(new WebSearchEnginePlugin(googleConnector), "google");
                }
            }
            else
            {
                _logger.LogInformation("Google plugin not enabled in configuration.");
            }

            // SystemManager plugin
            _logger.LogDebug($"Adding {nameof(SystemManagerPlugin)}");
            kernelBuilder.Plugins.AddFromType<SystemManagerPlugin>(nameof(SystemManagerPlugin));

            // EyesPlugin plugin
            if (PlatformUtil.IsRaspberryPi())
            {
                _logger.LogDebug($"Adding {nameof(EyesPlugin)}");
                kernelBuilder.Plugins.AddFromType<EyesPlugin>(nameof(EyesPlugin));
            }

            // Time plugin
            if (agentConfig.TimePluginEnabled)
            {
                _logger.LogDebug($"Adding {nameof(TimePlugin)}");
                kernelBuilder.Plugins.AddFromType<TimePlugin>(nameof(TimePlugin));
            }

            // Memory plugin
            if (agentConfig.MemoryPluginEnabled)
            {
                _logger.LogDebug("Adding MemoryPlugin");

                var dataDir = Path.Combine(AppContext.BaseDirectory, MemoryPluginDataFolder);
                if (!Directory.Exists(dataDir))
                    Directory.CreateDirectory(dataDir);

                var memoryStore = SqliteMemoryStore.ConnectAsync($"{dataDir}/{agentName}.sqlite", cancellationToken).GetAwaiter().GetResult();
                var embeddingGenerator = new OpenAITextEmbeddingGenerationService(modelId: TextEmbeddingModelId, apiKey: appConfig.OpenAiApiKey);
                var textMemory = new MemoryBuilder()
                    .WithLoggerFactory(_loggerFactory)
                    .WithTextEmbeddingGeneration(embeddingGenerator)
                    .WithMemoryStore(memoryStore)
                    .Build();
                var memoryPlugin = new TextMemoryPlugin(textMemory, _loggerFactory);

                kernelBuilder.Plugins.AddFromObject(memoryPlugin, "MemoryPlugin");
            }

            // Calculator plugin
            if (agentConfig.CalculatorPluginEnabled)
            {
                _logger.LogDebug($"Adding {nameof(CalculatorPlugin)}");
                kernelBuilder.Plugins.AddFromType<CalculatorPlugin>(nameof(CalculatorPlugin));
            }

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

    public async Task<Kernel> CreateKernel(Action<IKernelBuilder> configAction = null)
    {
        var appConfig = _appConfigOptions.Value;

        var kernelBuilder = Kernel.CreateBuilder();

        // Optional further configuration by caller
        configAction?.Invoke(kernelBuilder);

        // Forward register registrations from the parent container
        kernelBuilder.Services.AddTransient<IConfiguration>(_ => _serviceProvider.GetRequiredService<IConfiguration>());
        kernelBuilder.Services.AddTransient<ILoggerFactory>(_ => _serviceProvider.GetRequiredService<ILoggerFactory>());
        //kernelBuilder.Services.AddTransient<IExternalAudioPlayerService>(_ => _serviceProvider.GetRequiredService<IExternalAudioPlayerService>());
        kernelBuilder.Services.AddTransient<IEventBus>(_ => _serviceProvider.GetRequiredService<IEventBus>());
        kernelBuilder.Services.AddTransient<IVoiceService>(_ => _serviceProvider.GetRequiredService<IVoiceService>());
        kernelBuilder.Services.AddTransient<IGpioDeviceService>(_ => _serviceProvider.GetRequiredService<IGpioDeviceService>());

        // Register filters
        kernelBuilder.Services.AddSingleton<IFunctionInvocationFilter, FunctionInvocationLoggingFilter>();

        kernelBuilder.Services.ConfigureHttpClientDefaults(c =>
        {
            // TooManyRequests resiliency handler
            c.AddStandardResilienceHandler().Configure(o =>
            {
                o.Retry.ShouldHandle = args =>
                    ValueTask.FromResult(args.Outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests);

                // Retry backoff type
                o.Retry.BackoffType = DelayBackoffType.Exponential;

                // Individual attempt timeout (5 seconds for responsiveness)
                o.AttemptTimeout = new HttpTimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(15) };

                // Circuit breaker sampling duration (30 seconds to stabilize under load)
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);

                // Total timeout for all retries (15 seconds for quick failover)
                o.TotalRequestTimeout = new HttpTimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(30) };
            });
        });



        // If IChatCompletionService has not already been registered externally
        if (kernelBuilder.Services.All(descriptor => descriptor.ServiceType != typeof(IChatCompletionService)))
        {
            // Add Azure OpenAI Chat Completion
            kernelBuilder.AddOpenAIChatCompletion(
                apiKey: appConfig.OpenAiApiKey,
                modelId: appConfig.OpenAiModelId);
        }

        //if (_appConfig.SemanticPluginsPath != null)
        //{
        //    // Add directory of Semantic plugins
        //    var appDir = Path.GetDirectoryName(GetType().Assembly.Location);
        //    var pluginPath = Path.Combine(appDir, _appConfig.SemanticPluginsPath);
        //    if (Directory.Exists(pluginPath))
        //        kernelBuilder.Plugins.AddFromPromptDirectory(pluginPath);
        //    else
        //        _logger.LogWarning($"Specified plug-in directory {pluginPath} does not exist.");
        //}

        var kernel = kernelBuilder.Build();

        return kernel;
    }
}