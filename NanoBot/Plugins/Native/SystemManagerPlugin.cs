using Microsoft.SemanticKernel;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using NanoBot.Services;
using NanoBot.Util;

namespace NanoBot.Plugins.Native;

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

    [KernelFunction($"{nameof(NotifyConversationEnd)}")]
    [Description("Use this tool to ALWAYS notify the system that the user decided to finish the conversation. For example the user said 'ok', 'ok, thanks', 'bye', etc.")]
    public async Task NotifyConversationEnd(Kernel kernel)
    {
        _logger.LogDebug($"{nameof(NotifyConversationEnd)} tool invoked.");

        await _systemService.NotifyConversationEnd();
    }

    [KernelFunction($"{nameof(TurnOff)}")]
    [Description("Turns off the system. Only call this tool when you are clearly asked to turn yourself (the system) off.")]
    public async Task TurnOff(Kernel kernel)
    {
        _logger.LogDebug($"{nameof(TurnOff)} tool invoked.");

        if (PlatformUtil.IsLinuxPlatform())
            ShellExecute("sudo", "shutdown now");
    }

    [KernelFunction($"{nameof(Restart)}")]
    [Description("Restarts the system. Only call this tool when you are clearly asked to restart yourself (the system).")]
    public async Task Restart(Kernel kernel)
    {
        _logger.LogDebug($"{nameof(Restart)} tool invoked.");

        if (PlatformUtil.IsLinuxPlatform())
            ShellExecute("sudo", "reboot");
    }

    [KernelFunction($"{nameof(VolumeUp)}")]
    [Description("Increases the playback volume by 1 level (range 0-10).")]
    public async Task VolumeUp(Kernel kernel)
    {
        _logger.LogDebug($"{nameof(VolumeUp)} tool invoked.");

        _alsaControllerService.VolumeUp();
    }

    [KernelFunction($"{nameof(VolumeDown)}")]
    [Description("Decreases the playback volume by 1 level (range 0-10).")]
    public async Task VolumeDown(Kernel kernel)
    {
        _logger.LogDebug($"{nameof(VolumeDown)} tool invoked.");

        _alsaControllerService.VolumeDown();
    }

    [KernelFunction($"{nameof(SetVolume)}")]
    [Description("Sets the playback volume to a specific level. Volume must be between 1 and 10.")]
    public async Task SetVolume(
        Kernel kernel,
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
    
    [KernelFunction($"{nameof(GetPlaybackVolume)}")]
    [Description("Gets the current playback volume level (0-10).")]
    public async Task<int> GetPlaybackVolume(Kernel kernel)
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