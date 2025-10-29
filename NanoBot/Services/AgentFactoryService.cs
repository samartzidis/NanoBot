using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel;
using NanoBot.Configuration;
using Microsoft.SemanticKernel.Plugins.Web;
using Microsoft.SemanticKernel.Plugins.Web.Google;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.AI;
using NanoBot.Plugins.Native;
using System.Text;
using Microsoft.SemanticKernel.ChatCompletion;
using NanoBot.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
        var kernel = await CreateKernel(agentConfig, kernelBuilder =>
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

            // Add user plugins
            if (agentConfig.UserPluginsEnabled)
            {
                _logger.LogInformation("Adding user plugins");

                var pluginPath = ResolvePluginPath(appConfig.UserPluginPath);
                if (Directory.Exists(pluginPath))
                {
                    _logger.LogInformation($"Adding user plugins from {pluginPath}");

                    foreach (var subDir in Directory.EnumerateDirectories(pluginPath))
                    {
                        try
                        {
                            _logger.LogInformation($"Adding user plugin from {subDir}");
                            kernelBuilder.Plugins.AddFromPromptDirectoryYaml(subDir);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to add user plugin from {subDir}");
                        }
                    }
                }
                else
                {
                    _logger.LogWarning($"User plugins path {pluginPath} does not exist");
                }
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

        // Register filters
        kernelBuilder.Services.AddSingleton<IFunctionInvocationFilter, FunctionInvocationLoggingFilter>();
        
        kernelBuilder.Services.ConfigureHttpClientDefaults(c =>
        {
            /*
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
            */
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

    /// <summary>
    /// Resolves the plugin path, handling both absolute and relative paths.
    /// Relative paths are resolved relative to the application base directory.
    /// </summary>
    /// <param name="pluginPath">The configured plugin path (absolute or relative)</param>
    /// <returns>The resolved absolute path</returns>
    private static string ResolvePluginPath(string pluginPath)
    {
        if (string.IsNullOrWhiteSpace(pluginPath))
        {
            return Path.Combine(AppContext.BaseDirectory, "Plugins", "User");
        }

        // If the path is already absolute, return it as-is
        if (Path.IsPathRooted(pluginPath))
        {
            return pluginPath;
        }

        // For relative paths, combine with the application base directory
        return Path.Combine(AppContext.BaseDirectory, pluginPath);
    }
}