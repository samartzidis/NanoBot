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

    public SystemManagerPlugin(
        ILogger<SystemManagerPlugin> logger, 
        ISystemService systemService)
    {
        _logger = logger;
        _systemService = systemService;
    }

    [KernelFunction($"{nameof(ClearChatHistory)}")]
    [Description("Clears the current conversation chat history.")]
    public async Task ClearChatHistory(Kernel kernel)
    {
        _logger.LogDebug($"{nameof(ClearChatHistory)} tool invoked.");

        _systemService.History?.Clear();
    }

    [KernelFunction($"{nameof(TurnOff)}")]
    [Description("Turns off the system")]
    public async Task TurnOff(Kernel kernel)
    {
        _logger.LogDebug($"{nameof(TurnOff)} tool invoked.");

        _systemService.StopApplication();

        if (PlatformUtil.IsLinuxPlatform())
            _systemService.ShellExecute("sudo", "shutdown now");
    }

    [KernelFunction($"{nameof(Restart)}")]
    [Description("Restarts the system")]
    public async Task Restart(Kernel kernel)
    {
        _logger.LogDebug($"{nameof(Restart)} tool invoked.");

        _systemService.StopApplication();

        if (PlatformUtil.IsLinuxPlatform())
            _systemService.ShellExecute("sudo", "reboot");
    }
}