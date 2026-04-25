using System.ComponentModel;
using Microsoft.Extensions.Logging;
using NanoBot.Services;
using NanoBot.Util;

namespace NanoBot.Plugins;

public class SystemManagerPlugin
{
    private readonly ILogger _logger;
    private readonly ISystemService _systemService;
    private readonly IAlsaControllerService _alsaControllerService;

    public SystemManagerPlugin(
        ILogger<SystemManagerPlugin> logger, 
        ISystemService systemService,
        IAlsaControllerService alsaControllerService)
    {
        _logger = logger;
        _systemService = systemService;
        _alsaControllerService = alsaControllerService;
    }

    [Description(
        "*ALWAYS* call this tool when the user decided to end the conversation by saying any of these (or similar): 'Stop', 'OK, stop', 'OK', 'OK, thanks', 'Bye'.")]
    public async Task NotifyConversationStopRequested()
    {
        _logger.LogDebug($"{nameof(NotifyConversationStopRequested)} tool invoked.");

        await _systemService.NotifyConversationEnd();
    }

    [Description("Turns off the system. Only call this tool when you are clearly asked to turn yourself (the system) off.")]
    public async Task TurnOff()
    {
        _logger.LogDebug($"{nameof(TurnOff)} tool invoked.");

        if (PlatformUtil.IsLinuxPlatform())
            ShellExecute("sudo", "shutdown now");
    }

    [Description("Restarts the system. Only call this tool when you are clearly asked to restart yourself (the system).")]
    public async Task Restart()
    {
        _logger.LogDebug($"{nameof(Restart)} tool invoked.");

        if (PlatformUtil.IsLinuxPlatform())
            ShellExecute("sudo", "reboot");
    }

    [Description("Increases the playback volume by 1 level (range 0-10). Example: user says 'volume up'.")]
    public async Task VolumeUp()
    {
        _logger.LogDebug($"{nameof(VolumeUp)} tool invoked.");

        _alsaControllerService.VolumeUp();
    }

    [Description("Decreases the playback volume by 1 level (range 0-10). Example: user says 'volume down'.")]
    public async Task VolumeDown()
    {
        _logger.LogDebug($"{nameof(VolumeDown)} tool invoked.");

        _alsaControllerService.VolumeDown();
    }

    [Description("Sets the playback volume to a specific level. Volume must be between 1 and 10. Example: user says 'volume 5' etc.")]
    public async Task SetVolume(
        [Description("The volume level to set (1-10).")] int volume)
    {
        _logger.LogDebug($"{nameof(SetVolume)} tool invoked with volume: {volume}");

        if (volume < 1 || volume > 10)
        {
            _logger.LogWarning($"Invalid volume value: {volume}. Must be between 1 and 10.");
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be between 1 and 10.");
        }

        _alsaControllerService.SetPlaybackVolume(volume);
    }
    
    [Description("Gets the current playback volume level (0-10). Example: user says 'current volume', 'current volume level', 'volume level', etc.")]
    public async Task<int> GetPlaybackVolume()
    {
        _logger.LogDebug($"{nameof(GetPlaybackVolume)} tool invoked.");
        
        return _alsaControllerService.GetPlaybackVolume();
    }

    private async void ShellExecute(string cmd, string pars)
    {
        if (PlatformUtil.IsRaspberryPi())
        {
            try
            {
                // Create a new process to execute the shutdown command
                var processStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = cmd,
                    Arguments = pars,
                    UseShellExecute = false,  // Ensure the process is started without a shell
                    RedirectStandardOutput = false,  // Capture output (optional)
                    RedirectStandardError = false  // Capture error output (optional)
                };

                // Start the process
                using var process = System.Diagnostics.Process.Start(processStartInfo);
                if (process != null)
                    await process.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error: {ex.Message}");
            }
        }
    }
}