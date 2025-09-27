using Microsoft.Extensions.Logging;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using NanoBot.Configuration;

namespace NanoBot.Services;

public interface IDynamicEmbeddingService
{
    IEmbeddingGenerator<string, Embedding<float>> GetEmbeddingGenerator();
    bool IsEmbeddingAvailable { get; }
    Task<bool> TryInitializeEmbeddingAsync();
}

public class DynamicEmbeddingService : IDynamicEmbeddingService
{
    private readonly ILogger<DynamicEmbeddingService> _logger;
    private readonly IOptionsMonitor<AppConfig> _appConfigMonitor;
    private IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private bool _isInitialized = false;

    public DynamicEmbeddingService(
        ILogger<DynamicEmbeddingService> logger,
        IOptionsMonitor<AppConfig> appConfigMonitor)
    {
        _logger = logger;
        _appConfigMonitor = appConfigMonitor;

        // Subscribe to configuration changes
        _appConfigMonitor.OnChange(async (config, _) =>
        {
            await HandleConfigurationChangeAsync(config);
        });
    }

    public IEmbeddingGenerator<string, Embedding<float>> GetEmbeddingGenerator()
    {
        return _embeddingGenerator;
    }

    public bool IsEmbeddingAvailable => _embeddingGenerator != null;

    public async Task<bool> TryInitializeEmbeddingAsync()
    {
        try
        {
            var config = _appConfigMonitor.CurrentValue;
            
            if (string.IsNullOrEmpty(config.OpenAiApiKey))
            {
                _logger.LogDebug("OpenAI API key not available, embedding service will not be initialized");
                _embeddingGenerator = null;
                _isInitialized = true;
                return false;
            }

            // Try to create a new embedding generator using the service collection approach
            var services = new ServiceCollection();
            services.AddOpenAIEmbeddingGenerator(
                modelId: "text-embedding-3-small",
                apiKey: config.OpenAiApiKey);
            
            var serviceProvider = services.BuildServiceProvider();
            _embeddingGenerator = serviceProvider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

            _logger.LogInformation("OpenAI embedding service initialized successfully");
            _isInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize OpenAI embedding service");
            _embeddingGenerator = null;
            _isInitialized = true;
            return false;
        }
    }

    private async Task HandleConfigurationChangeAsync(AppConfig config)
    {
        if (!_isInitialized)
        {
            await TryInitializeEmbeddingAsync();
            return;
        }

        var hasApiKey = !string.IsNullOrEmpty(config.OpenAiApiKey);
        var wasAvailable = IsEmbeddingAvailable;

        if (hasApiKey && !wasAvailable)
        {
            _logger.LogInformation("OpenAI API key added, attempting to initialize embedding service");
            await TryInitializeEmbeddingAsync();
        }
        else if (!hasApiKey && wasAvailable)
        {
            _logger.LogInformation("OpenAI API key removed, disabling embedding service");
            _embeddingGenerator = null;
        }
        else if (hasApiKey && wasAvailable)
        {
            // API key might have changed, reinitialize
            _logger.LogInformation("OpenAI API key changed, reinitializing embedding service");
            await TryInitializeEmbeddingAsync();
        }
    }
}

