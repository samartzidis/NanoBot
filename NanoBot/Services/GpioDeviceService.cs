using NanoBot.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Device.Gpio;
using System.Device.Pwm;
using NanoBot.Util;

namespace NanoBot.Services;

public enum GpioDeviceLedColor
{
    Off,
    White,
    Red,
    LightGreen,
    Green,
    Blue,    
    Yellow,
    Cyan,
    Magenta,    
    Orange
}

public interface IGpioDeviceService : IHostedService
{
    public GpioDeviceLedColor DefaultLedColour { get; set; }
}

public class GpioDeviceService : BackgroundService, IGpioDeviceService
{
    public GpioDeviceLedColor DefaultLedColour { get; set; } = GpioDeviceLedColor.White;

    private const int RedPin = 18;  // GPIO18 (Physical Pin 12) - Hardware PWM0 (default)
    private const int GreenPin = 19; // GPIO19 (Physical Pin 35) - Hardware PWM1 (default)
    private const int BluePin = 16; // GPIO16 (Physical Pin 36) - Simple GPIO output (no PWM)
    private const int ButtonPin = 26; // GPIO26 (Pin 37 on the header)
    //private const int SpeakerPin = 12; // GPIO12 (Physical Pin 32)

    private readonly ILogger _logger;
    private readonly IEventBus _bus;
    private readonly GpioController _gpioController;
    private PwmChannel _redPwmChannel;
    private PwmChannel _greenPwmChannel;

    private const int PwmFrequency = 1000; // 1kHz frequency for smooth LED control

    private bool _buttonPressed;
    private bool _isShutdown, _isListening, _isFunctionInvoking, _isWakeWordDetected, _isError, _isNoiseDetected;
    private byte _talkLevel;

    public GpioDeviceService(ILogger<GpioDeviceService> logger, IEventBus bus)
    {
        _logger = logger;
        _bus = bus;

        if (PlatformUtil.IsRaspberryPi())
        {
            _gpioController = new GpioController();

            // Initialize PWM channels for Red and Green LEDs
            // Red and Green use hardware PWM (GPIO 18 & 19) - requires dtoverlay=pwm-2chan in /boot/config.txt
            // GPIO 18 = PWM0 (channel 0), GPIO 19 = PWM1 (channel 1) - default pins
            _redPwmChannel = PwmChannel.Create(0, 0, PwmFrequency); // Chip 0, Channel 0 (GPIO 18)
            _redPwmChannel.DutyCycle = 0.0;
            _redPwmChannel.Start();
            

            _greenPwmChannel = PwmChannel.Create(0, 1, PwmFrequency); // Chip 0, Channel 1 (GPIO 19)
            _greenPwmChannel.DutyCycle = 0.0;
            _greenPwmChannel.Start();            

            // Blue LED uses simple GPIO output (GPIO 21) - no PWM
            _gpioController.OpenPin(BluePin, PinMode.Output);

            // Speaker pin remains as simple GPIO output
            //_gpioController.OpenPin(SpeakerPin, PinMode.Output);
        }

        WireUpEventHandlers();
    }        
    
    private void WireUpEventHandlers()
    {
        _bus.Subscribe<ShutdownEvent>(e => { ResetTransientStates();  _isShutdown = true; UpdateLed(); });

        _bus.Subscribe<SystemErrorEvent>(e => { ResetTransientStates(); _isError = true; UpdateLed(); });
        _bus.Subscribe<SystemOkEvent>(e => { ResetTransientStates(); _isError = false; UpdateLed(); });

        _bus.Subscribe<StartListeningEvent>(e => { ResetTransientStates(); _isListening = true; UpdateLed(); });
        _bus.Subscribe<StopListeningEvent>(e => { ResetTransientStates(); _isListening = false; UpdateLed(); });

        _bus.Subscribe<FunctionInvokingEvent>(e => { ResetTransientStates(); _isFunctionInvoking = true; UpdateLed(); });
        _bus.Subscribe<FunctionInvokedEvent>(e => { ResetTransientStates(); _isFunctionInvoking = false; UpdateLed(); });

        _bus.Subscribe<WakeWordDetectedEvent>(e => { ResetTransientStates(); _isWakeWordDetected = true; UpdateLed(); });

        _bus.Subscribe<NoiseDetectedEvent>(e => { ResetTransientStates(); _isNoiseDetected = true; UpdateLed(); });
        _bus.Subscribe<SilenceDetectedEvent>(e => { ResetTransientStates(); _isNoiseDetected = false; UpdateLed(); });

        _bus.Subscribe<TalkLevelEvent>(e => { ResetTransientStates(); _talkLevel = e.Level; UpdateLed(); });
    }

    private void ResetTransientStates()
    {
        _isWakeWordDetected = false;
        _isNoiseDetected = false;
        _talkLevel = 0;
    }

    private void UpdateLed()
    {
        if (_isShutdown)
            SetLedColor(GpioDeviceLedColor.Off);
        else if (_isError)
            SetLedColor(GpioDeviceLedColor.Red);
        else if (_isFunctionInvoking)
            SetLedColor(GpioDeviceLedColor.Blue);
        else if (_talkLevel > 0)
        {
            const byte minInput = 0;
            const byte maxInput = 255;
            const byte minOutput = 32;
            const byte maxOutput = 255;
            var mappedLevel = (byte)(minOutput + (_talkLevel - minInput) * (maxOutput - minOutput) / (maxInput - minInput));
            SetLedColor((byte)(mappedLevel / 2), mappedLevel, false);
        }
        else if (_isListening)
            SetLedColor(GpioDeviceLedColor.LightGreen);
        else if (_isWakeWordDetected)
            SetLedColor(GpioDeviceLedColor.Orange);
        else if (_isNoiseDetected)
            SetLedColor(GpioDeviceLedColor.Yellow);
        else
            SetLedColor(DefaultLedColour);
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!PlatformUtil.IsRaspberryPi())
        {
            _logger.LogDebug($"{nameof(ExecuteAsync)} exiting immediately because executing platform is not Raspberry Pi.");
            return;
        }

        _gpioController.OpenPin(ButtonPin, PinMode.InputPullUp);

        while (!cancellationToken.IsCancellationRequested)
        {
            var pinValue = _gpioController.Read(ButtonPin);
            if (pinValue == PinValue.Low && !_buttonPressed)
            {
                _logger.LogDebug("Button pressed.");
                _buttonPressed = true;

                _bus.Publish<HangupInputEvent>(this);
            }
            else if (pinValue == PinValue.High && _buttonPressed)
            {
                _logger.LogDebug("Button released.");
                _buttonPressed = false;
            }

            await Task.Delay(100, cancellationToken);
        }
    }

    private void SetLedColor(byte red, byte green, bool blue)
    {
        _logger.LogDebug($"{nameof(SetLedColor)}: {red}, {green}, {blue}");

        if (PlatformUtil.IsRaspberryPi())
        {
            if (_redPwmChannel != null)
                _redPwmChannel.DutyCycle = (double)red / 255.0;
            
            if (_greenPwmChannel != null)
                _greenPwmChannel.DutyCycle = (double)green / 255.0;

            _gpioController.Write(BluePin, blue);
        }
    }

    private void SetLedColor(GpioDeviceLedColor color)
    {
        _logger.LogDebug($"{nameof(SetLedColor)}: {@color}");

        var red = 0.0;
        var green = 0.0;
        var blue = PinValue.Low;

        switch (color)
        {
            case GpioDeviceLedColor.Red:
                red = 1.0;
                break;
            case GpioDeviceLedColor.LightGreen:
                green = 0.125;
                break;
            case GpioDeviceLedColor.Green:
                green = 1.0;
                break;
            case GpioDeviceLedColor.Blue:
                blue = PinValue.High;
                break;            
            case GpioDeviceLedColor.Cyan:
                green = 1.0;
                blue = PinValue.High;
                break;
            case GpioDeviceLedColor.Magenta:
                red = 1.0;
                blue = PinValue.High;
                break;
            case GpioDeviceLedColor.White:
                red = 1.0;
                green = 1.0;
                blue = PinValue.High;
                break;
            case GpioDeviceLedColor.Yellow:
                red = 1.0;
                green = 0.5;
                break;
            case GpioDeviceLedColor.Orange:
                red = 1.0;
                green = 0.25;
                break;
            case GpioDeviceLedColor.Off:
                break;
            default:
                break;
        }

        if (PlatformUtil.IsRaspberryPi())
        {
            if (_redPwmChannel != null)
                _redPwmChannel.DutyCycle = red;

            if (_greenPwmChannel != null)
                _greenPwmChannel.DutyCycle = green;
            
            _gpioController.Write(BluePin, blue);
        }
    }

    //private void SetSpeaker(bool enabled)
    //{
    //    _logger.LogDebug($"{nameof(SetSpeaker)}: {enabled}");

    //    if (PlatformUtil.IsRaspberryPi())
    //    {
    //        _gpioController.Write(SpeakerPin, enabled ? PinValue.High : PinValue.Low);
    //    }
    //}

    public override void Dispose()
    {
        if (PlatformUtil.IsRaspberryPi())
        {
            // Clean up PWM channels
            if (_redPwmChannel != null)
            {
                _redPwmChannel.Stop();
                _redPwmChannel.Dispose();
            }
            if (_greenPwmChannel != null)
            {
                _greenPwmChannel.Stop();
                _greenPwmChannel.Dispose();
            }

            // Clean up GPIO resources
            _gpioController.ClosePin(BluePin);
            //_gpioController.ClosePin(SpeakerPin);
            _gpioController.Dispose();
            base.Dispose();
        }
    }
}