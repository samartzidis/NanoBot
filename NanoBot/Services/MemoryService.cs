using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using NanoBot.Configuration;

namespace NanoBot.Services;

public interface IMemoryService
{
    Task<string> SaveMemoryAsync(string key, string content);
    Task<string> GetMemoryAsync(string key);
    Task<List<MemoryItem>> GetRelevantMemoriesAsync(string userPrompt, int maxResults = 5);
    Task<List<MemoryItem>> GetAllMemoriesAsync();
    Task<bool> DeleteMemoryAsync(string key);
    Task<bool> UpdateMemoryAsync(string key, string content);
    Task ClearAllMemoriesAsync();
}

public class MemoryItem
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("accessCount")]
    public int AccessCount { get; set; }

    [JsonPropertyName("lastAccessedAt")]
    public DateTime? LastAccessedAt { get; set; }

    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; }
}

public class MemoryService : IMemoryService
{
    private readonly ILogger<MemoryService> _logger;
    private readonly string _memoryFilePath;
    private readonly Dictionary<string, MemoryItem> _memories;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly IDynamicEmbeddingService _dynamicEmbeddingService;
    private readonly int _maxMemories;

    public MemoryService(ILogger<MemoryService> logger, IDynamicEmbeddingService dynamicEmbeddingService, IOptions<AppConfig> appConfig)
    {
        _logger = logger;
        _memoryFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "memories.json");
        _memories = new Dictionary<string, MemoryItem>();
        _maxMemories = appConfig.Value.MemoryServiceMaxMemories;
        _dynamicEmbeddingService = dynamicEmbeddingService;

        _logger.LogInformation($"MemoryService initialized with max memories limit: {_maxMemories}");
        
        // Try to initialize embedding service if API key is available
        _ = Task.Run(async () => await _dynamicEmbeddingService.TryInitializeEmbeddingAsync());
        
        LoadMemoriesFromFile();
    }

    private void LoadMemoriesFromFile()
    {
        try
        {
            if (File.Exists(_memoryFilePath))
            {
                var json = File.ReadAllText(_memoryFilePath);
                var memoryList = JsonSerializer.Deserialize<List<MemoryItem>>(json);
                
                if (memoryList != null)
                {
                    foreach (var memory in memoryList)
                    {
                        _memories[memory.Key] = memory;
                    }
                }
                
                _logger.LogInformation($"Loaded {_memories.Count} memories from file");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load memories from file");
        }
    }

    private async Task SaveMemoriesToFileAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            var memoryList = _memories.Values.ToList();
            var json = JsonSerializer.Serialize(memoryList, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            await File.WriteAllTextAsync(_memoryFilePath, json);
            _logger.LogDebug($"Persisted {memoryList.Count} memories to file");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save memories to file");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task EnforceMemoryLimitAsync()
    {
        if (_memories.Count <= _maxMemories)
            return;

        var excessCount = _memories.Count - _maxMemories;
        _logger.LogInformation($"Memory limit exceeded. Current: {_memories.Count}, Limit: {_maxMemories}, Evicting: {excessCount} memories");

        // Find least frequently used memories to evict
        var memoriesToEvict = _memories.Values
            .OrderBy(m => m.AccessCount) // Least accessed first
            .ThenBy(m => m.LastAccessedAt ?? m.CreatedAt) // Then oldest first
            .Take(excessCount)
            .ToList();

        foreach (var memory in memoriesToEvict)
        {
            _memories.Remove(memory.Key);
            _logger.LogInformation($"Evicted memory (LFU): {memory.Key} (access count: {memory.AccessCount}, last accessed: {memory.LastAccessedAt?.ToString("yyyy-MM-dd HH:mm") ?? "never"})");
        }

        await SaveMemoriesToFileAsync();
        _logger.LogInformation($"Memory eviction complete. Current count: {_memories.Count}");
    }

    public async Task<string> SaveMemoryAsync(string key, string content)
    {
        try
        {
            var now = DateTime.UtcNow;
            var memory = new MemoryItem
            {
                Key = key,
                Content = content,
                CreatedAt = now,
                UpdatedAt = now,
                AccessCount = 0
            };

            // Generate embedding if embedding service is available
            if (_dynamicEmbeddingService.IsEmbeddingAvailable)
            {
                var embeddingGenerator = _dynamicEmbeddingService.GetEmbeddingGenerator();
                try
                {
                    _logger.LogDebug($"Attempting to generate embedding for memory: {key}");
                    var embedding = await embeddingGenerator.GenerateAsync(content);
                    memory.Embedding = embedding.Vector.ToArray();
                    _logger.LogInformation($"Successfully generated embedding for memory: {key} (dimensions: {memory.Embedding.Length})");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to generate embedding for memory: {key}. Error: {ex.Message}");
                }
            }
            else
            {
                _logger.LogDebug($"No embedding generator available for memory: {key}");
            }

            _memories[key] = memory;
            await SaveMemoriesToFileAsync();
            
            // Enforce memory limit after saving
            await EnforceMemoryLimitAsync();
            
            _logger.LogInformation($"Saved memory: {key}");
            return $"Memory '{key}' saved successfully";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to save memory: {key}");
            return $"Failed to save memory '{key}': {ex.Message}";
        }
    }

    public async Task<string> GetMemoryAsync(string key)
    {
        try
        {
            if (_memories.TryGetValue(key, out var memory))
            {
                memory.AccessCount++;
                memory.LastAccessedAt = DateTime.UtcNow;
                await SaveMemoriesToFileAsync();
                
                _logger.LogDebug($"Retrieved memory: {key}");
                return memory.Content;
            }
            
            _logger.LogDebug($"Memory not found: {key}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to get memory: {key}");
            return null;
        }
    }


    public async Task<List<MemoryItem>> GetRelevantMemoriesAsync(string userPrompt, int maxResults = 5)
    {
        try
        {
            if (!_dynamicEmbeddingService.IsEmbeddingAvailable)
            {
                _logger.LogDebug("No embedding generator available for semantic search");
                return new List<MemoryItem>();
            }

            var embeddingGenerator = _dynamicEmbeddingService.GetEmbeddingGenerator();
            // Generate embedding for the user prompt
            var promptEmbedding = await embeddingGenerator.GenerateAsync(userPrompt);
            var promptVector = promptEmbedding.Vector.ToArray();

            // Calculate semantic similarity scores for all memories with embeddings
            var memoriesWithScores = new List<(MemoryItem memory, float score)>();
            
            foreach (var memory in _memories.Values)
            {
                if (memory.Embedding != null && memory.Embedding.Length > 0)
                {
                    var similarity = CalculateCosineSimilarity(promptVector, memory.Embedding);
                    
                    // Apply additional scoring factors
                    var finalScore = similarity;
                    
                    // Boost score for frequently accessed memories
                    finalScore += memory.AccessCount * 0.01f;
                    
                    // Boost score for recent memories (decay over 30 days)
                    var daysSinceUpdate = (DateTime.UtcNow - memory.UpdatedAt).TotalDays;
                    finalScore += Math.Max(0, 0.1f - (float)daysSinceUpdate / 300f);
                    
                    memoriesWithScores.Add((memory, finalScore));
                    _logger.LogDebug("Memory '{Key}': similarity={Similarity:F3}, finalScore={FinalScore:F3}", memory.Key, similarity, finalScore);
                }
            }

            // Sort by semantic similarity score and take top results
            var results = memoriesWithScores
                .OrderByDescending(x => x.score)
                .Where(x => x.score >= 0.3f) // Only return memories with similarity >= 0.3
                .Take(maxResults)
                .Select(x => x.memory)
                .ToList();

            // Update access counts for found memories
            foreach (var memory in results)
            {
                memory.AccessCount++;
                memory.LastAccessedAt = DateTime.UtcNow;
            }

            if (results.Any())
            {
                await SaveMemoriesToFileAsync();
            }

            _logger.LogDebug($"Found {results.Count} semantically relevant memories for prompt (threshold: 0.3)");
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to get relevant memories for prompt: {userPrompt}");
            return new List<MemoryItem>();
        }
    }

    private static float CalculateCosineSimilarity(float[] vectorA, float[] vectorB)
    {
        if (vectorA.Length != vectorB.Length)
            return 0f;

        float dotProduct = 0f;
        float magnitudeA = 0f;
        float magnitudeB = 0f;

        for (int i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA[i] * vectorB[i];
            magnitudeA += vectorA[i] * vectorA[i];
            magnitudeB += vectorB[i] * vectorB[i];
        }

        magnitudeA = MathF.Sqrt(magnitudeA);
        magnitudeB = MathF.Sqrt(magnitudeB);

        if (magnitudeA == 0f || magnitudeB == 0f)
            return 0f;

        return dotProduct / (magnitudeA * magnitudeB);
    }

    public async Task<List<MemoryItem>> GetAllMemoriesAsync()
    {
        try
        {
            var memories = _memories.Values
                .OrderByDescending(m => m.UpdatedAt)
                .ToList();

            _logger.LogDebug($"Retrieved all {memories.Count} memories");
            return memories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all memories");
            return new List<MemoryItem>();
        }
    }

    public async Task<bool> DeleteMemoryAsync(string key)
    {
        try
        {
            if (_memories.Remove(key))
            {
                await SaveMemoriesToFileAsync();
                _logger.LogInformation($"Deleted memory: {key}");
                return true;
            }
            
            _logger.LogDebug($"Memory not found for deletion: {key}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to delete memory: {key}");
            return false;
        }
    }

    public async Task<bool> UpdateMemoryAsync(string key, string content)
    {
        try
        {
            if (_memories.TryGetValue(key, out var memory))
            {
                memory.Content = content;
                memory.UpdatedAt = DateTime.UtcNow;
                
                // Regenerate embedding if embedding service is available
                if (_dynamicEmbeddingService.IsEmbeddingAvailable)
                {
                    var embeddingGenerator = _dynamicEmbeddingService.GetEmbeddingGenerator();
                    try
                    {
                        var embedding = await embeddingGenerator.GenerateAsync(content);
                        memory.Embedding = embedding.Vector.ToArray();
                        _logger.LogDebug($"Regenerated embedding for memory: {key}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to regenerate embedding for memory: {key}");
                    }
                }
                
                await SaveMemoriesToFileAsync();
                _logger.LogInformation($"Updated memory: {key}");
                return true;
            }
            
            _logger.LogDebug($"Memory not found for update: {key}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to update memory: {key}");
            return false;
        }
    }


    public async Task ClearAllMemoriesAsync()
    {
        try
        {
            _memories.Clear();
            await SaveMemoriesToFileAsync();
            _logger.LogInformation("Cleared all memories");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear all memories");
        }
    }
}
