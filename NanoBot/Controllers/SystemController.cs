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

    [HttpGet("StreamLogs")]
    public async Task StreamLogs(CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";

        // Path to the log directory
        var logDirectory = Directory.GetCurrentDirectory();
        var logFiles = Directory.GetFiles(logDirectory, "log*.txt")
            .Select(file => new FileInfo(file))  // Get FileInfo to access metadata
            .OrderByDescending(fileInfo => fileInfo.LastWriteTime)  // Order by LastWriteTime (most recent modification)
            .FirstOrDefault();  // Get the most recent log file

        if (logFiles == null)
        {
            await Response.WriteAsync("No log files found.\n", cancellationToken);
            return;
        }

        try
        {
            // Send the filename as an SSE event
            await Response.WriteAsync($"File: {logFiles.Name}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken); // Ensure the filename is sent immediately

            // Open the log file in a way that allows both reading and writing, to prevent conflicts with Serilog's logging process
            await using var fileStream = new FileStream(logFiles.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var streamReader = new StreamReader(fileStream);

            // Start streaming new lines
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await streamReader.ReadLineAsync(cancellationToken);

                if (line != null)
                {
                    // Send each new log line as an SSE event
                    await Response.WriteAsync($"{line}\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken); // Ensure data is sent immediately
                }
                else
                {
                    // If no new data, wait briefly before checking again
                    await Task.Delay(100, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming log file in real-time.");
            await Response.WriteAsync($"An error occurred while streaming the log file ({ex.Message}).\n", cancellationToken);
        }
    }
}