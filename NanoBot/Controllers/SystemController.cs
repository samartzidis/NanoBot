using NanoBot.Configuration;
using NanoBot.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Audio;
using NanoBot.Util;

namespace NanoBot.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemController : ControllerBase
{
    private readonly ILogger<SystemController> _logger;
    private readonly IVoiceService _voiceService;
    private readonly AppConfig _appConfig;

    public SystemController(ILogger<SystemController> logger, IOptions<AppConfig> appConfig, IVoiceService voiceService)
    {
        _logger = logger;
        _voiceService = voiceService;
        _appConfig = appConfig.Value;
    }

    [HttpDelete("DeleteMemory")]
    public async Task<IActionResult> DeleteMemory(string agentName)
    {
        return Ok();
    }

    [HttpGet("GetVoices")]
    public async Task<IActionResult> GetVoices(TextToSpeechServiceProviderConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Fetching available voices...");

            var voices = await _voiceService.GetAvailableVoicesAsync(config, cancellationToken);

            if (voices == null || !voices.Any())
                return NotFound("No available voices found.");

            return Ok(voices); // Return 200 OK with the list of voices
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching available voices.");

            return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while fetching available voices.");
        }
    }

    [HttpGet("TextToSpeech")]
    public async Task GetTextToSpeech(string text, string voiceName, CancellationToken cancellationToken = default)
    {
        //"en-GB-SoniaNeural"

        if (voiceName == null)
        {
            if (_appConfig.VoiceService.TextToSpeechServiceProvider == TextToSpeechServiceProviderConfig.AzureSpeechService)
                voiceName = "en-GB-SoniaNeural";
            else
                voiceName = GeneratedSpeechVoice.Nova.ToString();
        }

        await _voiceService.GenerateTextToSpeechAsync(text, voiceName, cancellationToken);            
    }

    [HttpGet("SpeechToText")]
    public async Task<IActionResult> GetSpeechToText(string audioTranscriptionLanguage = "en", bool playback = false, CancellationToken cancellationToken = default)
    {
        var res = _voiceService.WaitForSpeech(out var userAudioBuffer, cancellationToken: cancellationToken);
        if (res != ReceiveVoiceMessageResult.Ok)            
            StatusCode(StatusCodes.Status500InternalServerError, res);

        if (playback)
        {
            var tempFilePath = Path.GetTempFileName();
            try
            {
                await System.IO.File.WriteAllBytesAsync(tempFilePath, userAudioBuffer, cancellationToken);

                await WavPlayerUtil.PlayAsync(tempFilePath, cancellationToken);
            }
            finally
            {
                if (System.IO.File.Exists(tempFilePath))
                    System.IO.File.Delete(tempFilePath);
            }
        }

        var text = _voiceService.GenerateSpeechToText(userAudioBuffer, audioTranscriptionLanguage);
        return Ok(text);
    }

    [HttpGet("DetectWakeWord")]
    public async Task<IActionResult> GetDetectWakeWord(int timeoutSec = 3, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec)); // Set a max wait of 10 seconds

        var word = await _voiceService.WaitForWakeWordAsync(cancellationToken: cts.Token);
        if (word != null)
            return Ok(word); // Return 200 OK with the wake word

        return StatusCode(StatusCodes.Status408RequestTimeout, "Request timed out while waiting for the wake word.");
    }

    [HttpGet("GetLogs")]
    public async Task<IActionResult> GetLogs(long lastPosition = 0, string lastFile = null)
    {
        try
        {
            // Path to the log directory
            var logDirectory = Directory.GetCurrentDirectory();
            var logFiles = Directory.GetFiles(logDirectory, "log*.txt")
                .Select(file => new FileInfo(file))
                .OrderByDescending(fileInfo => fileInfo.LastWriteTime)
                .FirstOrDefault();

            if (logFiles == null)
            {
                return Ok(new { fileName = (string)null, lines = new List<string>(), totalLines = 0, fileChanged = false, newPosition = 0L });
            }

            // Check if file has changed (rotation detection)
            bool fileChanged = lastFile != null && lastFile != logFiles.Name;
            
            // If file changed, start from beginning, otherwise start from last position
            long startPosition = fileChanged ? 0 : lastPosition;
            
            var (lines, newPosition) = await ReadLinesFromPositionAsync(logFiles.FullName, startPosition, CancellationToken.None);

            return Ok(new { 
                fileName = logFiles.Name, 
                lines = lines, 
                totalLines = lines.Count,
                hasNewLines = lines.Count > 0,
                fileChanged = fileChanged,
                newPosition = newPosition
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading log file.");
            return StatusCode(500, new { error = "Failed to read log file" });
        }
    }

    private async Task<(List<string> lines, long newPosition)> ReadLinesFromPositionAsync(string filePath, long startPosition, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        long currentPosition = startPosition;
        
        try
        {
            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            
            // If we're not at the beginning, seek to the start position
            if (startPosition > 0)
            {
                fileStream.Seek(startPosition, SeekOrigin.Begin);
            }
            
            using var streamReader = new StreamReader(fileStream);
            
            // Read ALL lines from the current position to the end of file
            string line;
            while ((line = await streamReader.ReadLineAsync(cancellationToken)) != null)
            {
                lines.Add(line);
                currentPosition = fileStream.Position;
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected while reading - return what we have so far
            _logger.LogDebug("Reading log file cancelled by client");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading from position {StartPosition}, falling back to reading entire file", startPosition);
            // Fallback to reading entire file if position-based reading fails
            var fallbackLines = await ReadEntireFileAsync(filePath, cancellationToken);
            return (fallbackLines, 0);
        }
        
        return (lines, currentPosition);
    }

    private async Task<List<string>> ReadEntireFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        
        try
        {
            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var streamReader = new StreamReader(fileStream);
            
            // Read all lines from the file
            string line;
            while ((line = await streamReader.ReadLineAsync(cancellationToken)) != null)
            {
                lines.Add(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected while reading - return what we have so far
            _logger.LogDebug("Reading entire file cancelled by client");
        }
        
        return lines;
    }
}