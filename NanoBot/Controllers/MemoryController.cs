using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NanoBot.Services;
using System.ComponentModel.DataAnnotations;

namespace NanoBot.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MemoryController : ControllerBase
{
    private readonly ILogger<MemoryController> _logger;
    private readonly IMemoryService _memoryService;

    public MemoryController(ILogger<MemoryController> logger, IMemoryService memoryService)
    {
        _logger = logger;
        _memoryService = memoryService;
    }

    /// <summary>
    /// Get all stored memories
    /// </summary>
    /// <returns>List of all memories</returns>
    [HttpGet]
    public async Task<ActionResult<MemoryListResponse>> GetAllMemories()
    {
        try
        {
            _logger.LogInformation("Getting all memories");
            var memories = await _memoryService.GetAllMemoriesAsync();
            
            var response = new MemoryListResponse
            {
                TotalCount = memories.Count,
                Memories = memories.Select(m => new MemoryDto
                {
                    Key = m.Key,
                    Content = m.Content,
                    CreatedAt = m.CreatedAt,
                    UpdatedAt = m.UpdatedAt,
                    AccessCount = m.AccessCount,
                    LastAccessedAt = m.LastAccessedAt,
                    HasEmbedding = m.Embedding != null
                }).ToList()
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all memories");
            return StatusCode(500, new { error = "Failed to retrieve memories" });
        }
    }

    /// <summary>
    /// Get a specific memory by key
    /// </summary>
    /// <param name="key">The memory key</param>
    /// <returns>The memory content</returns>
    [HttpGet("{key}")]
    public async Task<ActionResult<MemoryDto>> GetMemory(string key)
    {
        try
        {
            _logger.LogInformation("Getting memory with key: {Key}", key);
            var content = await _memoryService.GetMemoryAsync(key);
            
            if (content == null)
            {
                return NotFound(new { error = $"Memory with key '{key}' not found" });
            }

            // Get the full memory item to include metadata
            var allMemories = await _memoryService.GetAllMemoriesAsync();
            var memory = allMemories.FirstOrDefault(m => m.Key == key);
            
            if (memory == null)
            {
                return NotFound(new { error = $"Memory with key '{key}' not found" });
            }

            var response = new MemoryDto
            {
                Key = memory.Key,
                Content = memory.Content,
                CreatedAt = memory.CreatedAt,
                UpdatedAt = memory.UpdatedAt,
                AccessCount = memory.AccessCount,
                LastAccessedAt = memory.LastAccessedAt,
                HasEmbedding = memory.Embedding != null
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get memory with key: {Key}", key);
            return StatusCode(500, new { error = "Failed to retrieve memory" });
        }
    }

    /// <summary>
    /// Search memories by query
    /// </summary>
    /// <param name="query">Search query</param>
    /// <param name="maxResults">Maximum number of results (default: 10)</param>
    /// <returns>List of matching memories</returns>
    [HttpGet("search")]
    public async Task<ActionResult<MemoryListResponse>> SearchMemories([FromQuery] string query, [FromQuery] int maxResults = 10)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest(new { error = "Query parameter is required" });
            }

            _logger.LogInformation("Searching memories with query: {Query}", query);
            var memories = await _memoryService.GetRelevantMemoriesAsync(query, maxResults);
            
            var response = new MemoryListResponse
            {
                TotalCount = memories.Count,
                Memories = memories.Select(m => new MemoryDto
                {
                    Key = m.Key,
                    Content = m.Content,
                    CreatedAt = m.CreatedAt,
                    UpdatedAt = m.UpdatedAt,
                    AccessCount = m.AccessCount,
                    LastAccessedAt = m.LastAccessedAt,
                    HasEmbedding = m.Embedding != null
                }).ToList()
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search memories with query: {Query}", query);
            return StatusCode(500, new { error = "Failed to search memories" });
        }
    }

    /// <summary>
    /// Get memory statistics
    /// </summary>
    /// <returns>Memory statistics</returns>
    [HttpGet("stats")]
    public async Task<ActionResult<MemoryStatsResponse>> GetMemoryStats()
    {
        try
        {
            _logger.LogInformation("Getting memory statistics");
            var memories = await _memoryService.GetAllMemoriesAsync();
            
            var stats = new MemoryStatsResponse
            {
                TotalMemories = memories.Count,
                TotalSizeBytes = memories.Sum(m => m.Content.Length + (m.Embedding?.Length * 4 ?? 0)),
                MemoriesWithEmbeddings = memories.Count(m => m.Embedding != null),
                OldestMemory = memories.Any() ? memories.Min(m => m.CreatedAt) : null,
                NewestMemory = memories.Any() ? memories.Max(m => m.CreatedAt) : null,
                MostAccessedCount = memories.Any() ? memories.Max(m => m.AccessCount) : 0,
                AverageAccessCount = memories.Any() ? memories.Average(m => m.AccessCount) : 0,
                RecentlyAccessed = memories.Count(m => m.LastAccessedAt.HasValue && 
                    (DateTime.UtcNow - m.LastAccessedAt.Value).TotalDays <= 7)
            };

            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get memory statistics");
            return StatusCode(500, new { error = "Failed to retrieve memory statistics" });
        }
    }

    /// <summary>
    /// Delete a specific memory by key
    /// </summary>
    /// <param name="key">The memory key to delete</param>
    /// <returns>Success or error response</returns>
    [HttpDelete("{key}")]
    public async Task<ActionResult> DeleteMemory(string key)
    {
        try
        {
            _logger.LogInformation("Deleting memory with key: {Key}", key);
            var success = await _memoryService.DeleteMemoryAsync(key);
            
            if (!success)
            {
                return NotFound(new { error = $"Memory with key '{key}' not found" });
            }

            return Ok(new { message = $"Memory '{key}' deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete memory with key: {Key}", key);
            return StatusCode(500, new { error = "Failed to delete memory" });
        }
    }

    /// <summary>
    /// Clear all memories
    /// </summary>
    /// <returns>Success response</returns>
    [HttpDelete("clear")]
    public async Task<ActionResult> ClearAllMemories()
    {
        try
        {
            _logger.LogInformation("Clearing all memories");
            await _memoryService.ClearAllMemoriesAsync();
            
            return Ok(new { message = "All memories cleared successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear all memories");
            return StatusCode(500, new { error = "Failed to clear memories" });
        }
    }

    /// <summary>
    /// Update an existing memory
    /// </summary>
    /// <param name="key">The memory key to update</param>
    /// <param name="request">The update request</param>
    /// <returns>Success or error response</returns>
    [HttpPut("{key}")]
    public async Task<ActionResult> UpdateMemory(string key, [FromBody] UpdateMemoryRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return BadRequest(new { error = "Content is required" });
            }

            _logger.LogInformation("Updating memory with key: {Key}", key);
            var success = await _memoryService.UpdateMemoryAsync(key, request.Content);
            
            if (!success)
            {
                return NotFound(new { error = $"Memory with key '{key}' not found" });
            }

            return Ok(new { message = $"Memory '{key}' updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update memory with key: {Key}", key);
            return StatusCode(500, new { error = "Failed to update memory" });
        }
    }
}

// DTOs
public class MemoryDto
{
    public string Key { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int AccessCount { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public bool HasEmbedding { get; set; }
}

public class MemoryListResponse
{
    public int TotalCount { get; set; }
    public List<MemoryDto> Memories { get; set; } = new();
}

public class MemoryStatsResponse
{
    public int TotalMemories { get; set; }
    public long TotalSizeBytes { get; set; }
    public int MemoriesWithEmbeddings { get; set; }
    public DateTime? OldestMemory { get; set; }
    public DateTime? NewestMemory { get; set; }
    public int MostAccessedCount { get; set; }
    public double AverageAccessCount { get; set; }
    public int RecentlyAccessed { get; set; }
}

public class UpdateMemoryRequest
{
    [Required]
    public string Content { get; set; } = string.Empty;
}
