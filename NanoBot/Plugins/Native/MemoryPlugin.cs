using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using NanoBot.Services;

#nullable enable

namespace NanoBot.Plugins.Native;

public sealed class MemoryPlugin
{
    private readonly ILogger<MemoryPlugin> _logger;
    private readonly IMemoryService _memoryService;

    public MemoryPlugin(ILogger<MemoryPlugin> logger, IMemoryService memoryService)
    {
        _logger = logger;
        _memoryService = memoryService;
    }

    [KernelFunction, Description("Save a memory with a specific key and content. Use this to remember important information about the user, their preferences, or any facts they want you to remember.")]
    public async Task<string> SaveMemoryAsync(
        [Description("A unique key/name for this memory (e.g., 'user_name', 'favorite_color', 'work_schedule')")] string key,
        [Description("The content/information to remember")] string content,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("SaveMemoryAsync: Saving memory with key '{Key}'", key);
        
        var result = await _memoryService.SaveMemoryAsync(key, content);
        return result;
    }

    [KernelFunction, Description("Retrieve a specific memory by its key. Use this to recall information you previously saved.")]
    public async Task<string> GetMemoryAsync(
        [Description("The key/name of the memory to retrieve")] string key,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetMemoryAsync: Retrieving memory with key '{Key}'", key);
        
        var content = await _memoryService.GetMemoryAsync(key);
        if (content == null)
        {
            return $"No memory found with key '{key}'";
        }
        
        return content;
    }

    [KernelFunction, Description("Get memories that are semantically relevant to the current user prompt. This uses OpenAI embeddings to understand context and find the most relevant memories based on semantic similarity, even if they don't contain exact keywords.")]
    public async Task<string> GetRelevantMemoriesAsync(
        [Description("The current user prompt or question to find relevant memories for")] string userPrompt,
        [Description("Maximum number of relevant memories to return (default: 5)")] int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetRelevantMemoriesAsync: Finding relevant memories for prompt '{Prompt}'", userPrompt);
        
        var memories = await _memoryService.GetRelevantMemoriesAsync(userPrompt, maxResults);
        
        if (!memories.Any())
        {
            return $"No relevant memories found for the current context.";
        }

        var result = $"Found {memories.Count} relevant memories:\n\n";
        foreach (var memory in memories)
        {
            result += $"• **{memory.Key}**: {memory.Content}\n";
        }
        
        return result;
    }

    [KernelFunction, Description("Search for memories using semantic similarity. Use this to find relevant memories when you're not sure of the exact key.")]
    public async Task<string> SearchMemoriesAsync(
        [Description("Search query to find relevant memories")] string query,
        [Description("Maximum number of results to return (default: 10)")] int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("SearchMemoriesAsync: Searching for memories with query '{Query}'", query);
        
        var memories = await _memoryService.GetRelevantMemoriesAsync(query, maxResults);
        
        if (!memories.Any())
        {
            return $"No memories found matching '{query}'";
        }

        var result = $"Found {memories.Count} memories matching '{query}':\n\n";
        foreach (var memory in memories)
        {
            result += $"• **{memory.Key}**: {memory.Content}\n";
        }
        
        return result;
    }

    [KernelFunction, Description("Get all saved memories. Use this to see everything you have remembered.")]
    public async Task<string> GetAllMemoriesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetAllMemoriesAsync: Retrieving all memories");
        
        var memories = await _memoryService.GetAllMemoriesAsync();
        
        if (!memories.Any())
        {
            return "No memories have been saved yet.";
        }

        var result = $"You have {memories.Count} saved memories:\n\n";
        foreach (var memory in memories)
        {
            result += $"• **{memory.Key}**: {memory.Content}\n";
            result += $"  Created: {memory.CreatedAt:yyyy-MM-dd HH:mm}, Last accessed: {memory.AccessCount} times\n\n";
        }
        
        return result;
    }

    [KernelFunction, Description("Update an existing memory with new content. Use this to modify information you previously saved.")]
    public async Task<string> UpdateMemoryAsync(
        [Description("The key/name of the memory to update")] string key,
        [Description("The new content to replace the existing memory")] string content,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("UpdateMemoryAsync: Updating memory with key '{Key}'", key);
        
        var success = await _memoryService.UpdateMemoryAsync(key, content);
        
        if (success)
        {
            return $"Memory '{key}' updated successfully";
        }
        else
        {
            return $"No memory found with key '{key}' to update";
        }
    }

    [KernelFunction, Description("Delete a specific memory by its key. Use this to remove information you no longer need to remember.")]
    public async Task<string> DeleteMemoryAsync(
        [Description("The key/name of the memory to delete")] string key,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("DeleteMemoryAsync: Deleting memory with key '{Key}'", key);
        
        var success = await _memoryService.DeleteMemoryAsync(key);
        
        if (success)
        {
            return $"Memory '{key}' deleted successfully";
        }
        else
        {
            return $"No memory found with key '{key}' to delete";
        }
    }

    [KernelFunction, Description("Clear all saved memories. Use this with caution as it will permanently delete all remembered information.")]
    public async Task<string> ClearAllMemoriesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("ClearAllMemoriesAsync: Clearing all memories");
        
        await _memoryService.ClearAllMemoriesAsync();
        return "All memories have been cleared successfully";
    }

    [KernelFunction, Description("Get memory statistics including total count and most accessed memories.")]
    public async Task<string> GetMemoryStatsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetMemoryStatsAsync: Retrieving memory statistics");
        
        var memories = await _memoryService.GetAllMemoriesAsync();
        
        if (!memories.Any())
        {
            return "No memories have been saved yet.";
        }

        var totalMemories = memories.Count;
        var mostAccessed = memories.OrderByDescending(m => m.AccessCount)
                                  .Take(5)
                                  .ToList();

        var result = $"**Memory Statistics:**\n\n";
        result += $"• Total memories: {totalMemories}\n";
        
        result += $"\n• Most accessed memories:\n";
        foreach (var memory in mostAccessed)
        {
            result += $"  - {memory.Key}: {memory.AccessCount} times\n";
        }
        
        return result;
    }
}
