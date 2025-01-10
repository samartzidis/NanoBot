using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using NanoBot.Services;
using System.ComponentModel;

namespace NanoBot.Plugins.Native
{
    public class EyesPlugin
    {
        private readonly ILogger _logger;
        private readonly IGpioDeviceService _gpioDeviceService;

        public EyesPlugin(ILogger<EyesPlugin> logger, IGpioDeviceService gpioDeviceService) 
        {
            _gpioDeviceService = gpioDeviceService;
            _logger = logger;
        }

        [KernelFunction($"{nameof(SetEyeColour)}")]
        [Description("Set the eyes colour to the specified colour.")]
        public async Task SetEyeColour(Kernel kernel, GpioDeviceLedColor colour)
        {
            _logger.LogDebug($"{nameof(SetEyeColour)} tool invoked.");

            _gpioDeviceService.DefaultLedColour = colour;
        }

        [KernelFunction($"{nameof(GetEyeColour)}")]
        [Description("Get the current eyes colour.")]
        public async Task<GpioDeviceLedColor> GetEyeColour(Kernel kernel)
        {
            _logger.LogDebug($"{nameof(GetEyeColour)} tool invoked.");

            return _gpioDeviceService.DefaultLedColour;
        }

        [KernelFunction($"{nameof(GetAvailableEyesColours)}")]
        [Description("Get the available eye colours.")]
        public async Task<List<string>> GetAvailableEyesColours(Kernel kernel)
        {
            _logger.LogDebug($"{nameof(GetAvailableEyesColours)} tool invoked.");

            return Enum.GetNames< GpioDeviceLedColor>().ToList();
        }
    }
}
