using Microsoft.Extensions.Logging;
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

        [Description("Turn on the eyes (and sets the eye colour to normal). Example: the user says: 'eyes on'.")]
        public async Task TurnOn()
        {
            _logger.LogDebug($"{nameof(TurnOn)} tool invoked.");

            _gpioDeviceService.DefaultLedColour = GpioDeviceLedColor.White;
        }

        [Description("Turn off the eyes.. Example: the user says: 'eyes off'.")]
        public async Task TurnOff()
        {
            _logger.LogDebug($"{nameof(TurnOff)} tool invoked.");

            _gpioDeviceService.DefaultLedColour = GpioDeviceLedColor.Off;
        }

        [Description("Set the eyes colour to the specified colour.. Example: the user says: 'eyes blue' or 'eyes white', etc.")]
        public async Task SetEyeColour(GpioDeviceLedColor colour)
        {
            _logger.LogDebug($"{nameof(SetEyeColour)} tool invoked.");

            _gpioDeviceService.DefaultLedColour = colour;
        }

        [Description("Get the current eyes colour.")]
        public async Task<GpioDeviceLedColor> GetEyeColour()
        {
            _logger.LogDebug($"{nameof(GetEyeColour)} tool invoked.");

            return _gpioDeviceService.DefaultLedColour;
        }

        [Description("Get the available eye colours.")]
        public async Task<List<string>> GetAvailableEyesColours()
        {
            _logger.LogDebug($"{nameof(GetAvailableEyesColours)} tool invoked.");

            return Enum.GetNames<GpioDeviceLedColor>().ToList();
        }
    }
}
