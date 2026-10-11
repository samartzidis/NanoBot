using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NanoBot.Services;

namespace NanoBot.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConversationController : ControllerBase
{
    private const long MaxImageBytes = 4 * 1024 * 1024;

    private static readonly HashSet<string> AllowedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif"
    };

    private readonly ILogger<ConversationController> _logger;
    private readonly ISystemService _systemService;

    public ConversationController(ILogger<ConversationController> logger, ISystemService systemService)
    {
        _logger = logger;
        _systemService = systemService;
    }

    /// <summary>
    /// Adds an uploaded picture to the current conversation context.
    /// </summary>
    [HttpPost("Image")]
    [RequestSizeLimit(MaxImageBytes + 64 * 1024)]
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file provided." });

            if (file.Length > MaxImageBytes)
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = $"Image exceeds {MaxImageBytes / (1024 * 1024)} MB." });

            if (!AllowedImageTypes.Contains(file.ContentType ?? string.Empty))
                return BadRequest(new { error = "Unsupported image type. Use JPEG, PNG, WebP or GIF." });

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);

            await _systemService.AddImageAsync(ms.ToArray(), file.ContentType.ToLowerInvariant());

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding image to conversation.");
            return StatusCode(500, new { error = "Failed to add image." });
        }
    }
}
