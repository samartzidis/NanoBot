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

        [KernelFunction($"{nameof(TurnOn)}")]
        [Description("Turn on the eyes (and sets the eye colour to normal). Example: the user says: 'eyes on'.")]
        public async Task TurnOn(Kernel kernel)
        {
            _logger.LogDebug($"{nameof(TurnOn)} tool invoked.");

            _gpioDeviceService.DefaultLedColour = GpioDeviceLedColor.White;
        }

        [KernelFunction($"{nameof(TurnOff)}")]
        [Description("Turn off the eyes.. Example: the user says: 'eyes off'.")]
        public async Task TurnOff(Kernel kernel)
        {
            _logger.LogDebug($"{nameof(TurnOff)} tool invoked.");

            _gpioDeviceService.DefaultLedColour = GpioDeviceLedColor.Off;
        }

        [KernelFunction($"{nameof(SetEyeColour)}")]
        [Description("Set the eyes colour to the specified colour.. Example: the user says: 'eyes blue' or 'eyes white', etc.")]
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

            return Enum.GetNames<GpioDeviceLedColor>().ToList();
        }
    }
}
