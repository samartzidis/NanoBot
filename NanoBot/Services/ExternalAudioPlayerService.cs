using NanoBot.Configuration;
using NanoBot.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace NanoBot.Services;

public interface IExternalAudioPlayerService
{
    Task PlayAsync(string inputFilePath, CancellationToken cancellationToken = default);
    void Stop();
    bool IsPlaying { get; }
}

public class ExternalAudioPlayerService : IExternalAudioPlayerService, IDisposable
{
    private readonly ILogger _logger;
    private readonly AppConfig _appConfig;
    private readonly IEventBus _bus;

    private Process _mplayerProcess;
    private bool _isPlaying;
    private readonly object _lock = new();

    public string MPlayerPath { get; set; }

    public ExternalAudioPlayerService(ILogger<ExternalAudioPlayerService> logger, IOptions<AppConfig> appConfig, IEventBus bus)
    {
        _logger = logger;
        _appConfig = appConfig.Value;
        _bus = bus;

        MPlayerPath = _appConfig.MPlayerPath ?? (Util.PlatformUtil.IsWindowsPlatform() ? "mplayer.exe" : "mplayer");

        _bus.Subscribe<ShutdownEvent>(e => { Stop(); });
    }

    public bool IsPlaying
    {
        get
        {
            lock (_lock)
            {
                return _isPlaying;
            }
        }
        private set
        {
            lock (_lock)
            {
                _isPlaying = value;
            }
        }
    }

    public async Task PlayAsync(string inputFilePath, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug($"{nameof(PlayAsync)}");

        if (string.IsNullOrWhiteSpace(inputFilePath))
            throw new ArgumentException("Input file path cannot be null or empty.", nameof(inputFilePath));

        if (!File.Exists(inputFilePath))
            throw new FileNotFoundException("Audio file not found.", inputFilePath);

        Stop(); // Stop any existing playback

        // Prepare the mplayer process
        var startInfo = new ProcessStartInfo
        {
            FileName = MPlayerPath, // Use the specified mplayer path
            //Arguments = $"-really-quiet \"{inputFilePath}\"", // < this occasionally causes mplayer to get stuck in an infinite loop on Windows
            Arguments = $"\"{inputFilePath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = true
        };

        _mplayerProcess = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        // Set up cancellation and process events
        await using (cancellationToken.Register(Stop))
        {
            _mplayerProcess.Exited += (sender, args) =>
            {
                IsPlaying = false;
                _mplayerProcess?.Dispose();
                _mplayerProcess = null;
            };

            try
            {
                _mplayerProcess.Start();
                IsPlaying = true;

                // Await process exit or cancellation
                await Task.Run(() => _mplayerProcess.WaitForExit(), cancellationToken);
            }
            catch (TaskCanceledException)
            {
                Stop(); // Ensure the process is terminated if canceled

                _logger.LogDebug($"{nameof(PlayAsync)} cancelled.");
            }
            catch (Exception ex)
            {
                Stop(); // Clean up on failure

                _logger.LogError(ex, "Error occurred during playback");

                throw;
            }
        }
    }

    public void Stop()
    {
        _logger.LogDebug($"{nameof(Stop)}");

        lock (_lock)
        {
            if (_mplayerProcess != null && !_mplayerProcess.HasExited)
            {
                try
                {
                    _mplayerProcess.Kill(); // Terminate the process
                }
                catch
                {
                    // Ignore exceptions when killing the process
                }
                finally
                {
                    _mplayerProcess.Dispose();
                    _mplayerProcess = null;
                    IsPlaying = false;
                }
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}