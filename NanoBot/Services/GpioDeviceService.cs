using NanoBot.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using System.Device.Pwm;
using NanoBot.Util;

namespace NanoBot.Services;

public enum GpioDeviceLedColor
{
    Off,
    White,
    Red,
    Green,
    LightGreen,
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

    // Per-board wiring: red/green are hardware PWM channels on pwmchip0, blue and the button are GPIO line
    // offsets on the GPIO chip given by GpioChip (null = the library's default chip).
    private sealed record BoardPins(int RedPwmChannel, int GreenPwmChannel, int BluePin, int ButtonPin, int? GpioChip = null);

    // GPIO18 (Physical Pin 12) = PWM0, GPIO19 (Physical Pin 35) = PWM1, blue GPIO16 (Pin 36), button GPIO26 (Pin 37).
    private static readonly BoardPins RaspberryPiPins = new(RedPwmChannel: 0, GreenPwmChannel: 1, BluePin: 16, ButtonPin: 26);

    // Orange Pi Zero 2W (H616): red PWM on Pin 32, green PWM on Pin 33, blue PC12 (Pin 36) = 2*32+12,
    // button PI16 (Pin 37) = 8*32+16. The main 288-line pinctrl (300b000.pinctrl) is gpiochip1 here; gpiochip0
    // is the 32-line PL bank (needs libgpiod 2.x, libgpiod.so.3). Line offsets to be confirmed with gpioinfo.
    private static readonly BoardPins OrangePiPins = new(RedPwmChannel: 1, GreenPwmChannel: 2, BluePin: 76, ButtonPin: 272, GpioChip: 1);

    private readonly BoardPins _pins = PlatformUtil.IsOrangePi() ? OrangePiPins : RaspberryPiPins;

    private readonly ILogger _logger;
    private readonly IEventBus _bus;
    private readonly GpioController _gpioController;
    private readonly PwmChannel _redPwmChannel;
    private readonly PwmChannel _greenPwmChannel;

    private const int PwmFrequency = 1000; // 1kHz frequency for smooth LED control

    private bool _buttonPressed;
    private bool _isShutdown, _isListening, _isFunctionInvoking, _isWakeWordDetected, _isError, _isNoiseDetected, _isNightMode;
    private byte? _talkLevel;

    // Track the last LED state actually applied so we can short-circuit redundant updates.
    // The TalkLevelEvent published by Speaker fires ~10x per second while audio is in the
    // playback buffer; without this, every tick re-asserts (and re-logs) whichever higher-
    // priority colour UpdateLed picks (Blue during function-invoking, Red during error, etc.).
    // Exactly one of these will be non-null at any time, matching how UpdateLed sets the LED:
    // _lastAppliedColor when an enum colour was applied, _lastAppliedRgb when a raw byte
    // triple was applied (the talk-level brightness modulation path).
    private GpioDeviceLedColor? _lastAppliedColor;
    private (byte r, byte g, bool b)? _lastAppliedRgb;

    public GpioDeviceService(ILogger<GpioDeviceService> logger, IEventBus bus)
    {
        _logger = logger;
        _bus = bus;

        if (PlatformUtil.IsSupportedBoard())
        {
            _gpioController = _pins.GpioChip is { } chip
                ? new GpioController(new LibGpiodV2Driver(chip))
                : new GpioController();

            // Initialize PWM channels for Red and Green LEDs
            // Red and Green use hardware PWM - on the Raspberry Pi this requires dtoverlay=pwm-2chan in /boot/config.txt
            _redPwmChannel = PwmChannel.Create(0, _pins.RedPwmChannel, PwmFrequency);
            _redPwmChannel.DutyCycle = 0.0;
            _redPwmChannel.Start();


            _greenPwmChannel = PwmChannel.Create(0, _pins.GreenPwmChannel, PwmFrequency);
            _greenPwmChannel.DutyCycle = 0.0;
            _greenPwmChannel.Start();

            // Blue LED uses simple GPIO output - no PWM
            _gpioController.OpenPin(_pins.BluePin, PinMode.Output);
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

        _bus.Subscribe<NightModeActivatedEvent>(e => { _isNightMode = true; UpdateLed(); });
        _bus.Subscribe<NightModeDeactivatedEvent>(e => { _isNightMode = false; UpdateLed(); });
    }

    private void ResetTransientStates()
    {
        _isWakeWordDetected = false;
        _isNoiseDetected = false;
        _talkLevel = null;
    }

    private void UpdateLed()
    {
        // First decide what the LED *should* show, then short-circuit if it's already showing
        // exactly that. This avoids redundant log spam and PWM/GPIO writes when an event fires
        // that doesn't actually change the visible state (e.g. TalkLevelEvent ticks while a
        // higher-priority state like Blue/Red owns the LED).
        GpioDeviceLedColor? desiredColor = null;
        (byte r, byte g, bool b)? desiredRgb = null;

        if (_isShutdown)
            desiredColor = GpioDeviceLedColor.Off;
        else if (_isError)
            desiredColor = GpioDeviceLedColor.Red;
        else if (_isFunctionInvoking)
            desiredColor = GpioDeviceLedColor.Blue;
        else if (_talkLevel.HasValue)
            desiredRgb = (0, _talkLevel.Value, false);
        else if (_isListening)
            desiredColor = GpioDeviceLedColor.LightGreen;
        else if (_isWakeWordDetected)
            desiredColor = GpioDeviceLedColor.Orange;
        else if (_isNoiseDetected)
            desiredColor = GpioDeviceLedColor.Yellow;
        else if (_isNightMode)
            desiredColor = GpioDeviceLedColor.Off;
        else
            desiredColor = DefaultLedColour;

        if (desiredColor.HasValue)
        {
            if (desiredColor == _lastAppliedColor)
                return;
            _lastAppliedColor = desiredColor;
            _lastAppliedRgb = null;
            SetLedColor(desiredColor.Value);
        }
        else if (desiredRgb.HasValue)
        {
            if (desiredRgb == _lastAppliedRgb)
                return;
            _lastAppliedRgb = desiredRgb;
            _lastAppliedColor = null;
            SetLedColor(desiredRgb.Value.r, desiredRgb.Value.g, desiredRgb.Value.b, log: false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!PlatformUtil.IsSupportedBoard())
        {
            _logger.LogDebug($"{nameof(ExecuteAsync)} exiting immediately because executing platform is not a Raspberry Pi or Orange Pi.");
            return;
        }

        _gpioController.OpenPin(_pins.ButtonPin, PinMode.InputPullUp);

        while (!cancellationToken.IsCancellationRequested)
        {
            var pinValue = _gpioController.Read(_pins.ButtonPin);
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

    private void SetLedColor(GpioDeviceLedColor color, bool log = true)
    {
        if (log)
            _logger.LogDebug($"{nameof(SetLedColor)}: {@color}");

        switch (color)
        {
            case GpioDeviceLedColor.Red:
                SetLedColor(255, 0, false, log);
                break;
            case GpioDeviceLedColor.Green:
                SetLedColor(0, 255, false, log);
                break;
            case GpioDeviceLedColor.LightGreen:
                SetLedColor(0, 16, false, log);
                break;
            case GpioDeviceLedColor.Blue:
                SetLedColor(0, 0, true, log);
                break;            
            case GpioDeviceLedColor.Cyan:
                SetLedColor(0, 255, true, log);
                break;
            case GpioDeviceLedColor.Magenta:
                SetLedColor(255, 0, true, log);
                break;
            case GpioDeviceLedColor.White:
                SetLedColor(255, 255, true, log);
                break;
            case GpioDeviceLedColor.Yellow:
                SetLedColor(255, 128, false, log);
                break;
            case GpioDeviceLedColor.Orange:
                SetLedColor(255, 64, false, log);
                break;
            case GpioDeviceLedColor.Off:
                SetLedColor(0, 0, false, log);
                break;
            default:
                break;
        }
    }

    private void SetLedColor(byte red, byte green, bool blue, bool log = true)
    {
        if (log)
            _logger.LogDebug($"{nameof(SetLedColor)}: r={red}, g={green}, b={blue}");

        if (PlatformUtil.IsSupportedBoard())
        {
            if (_redPwmChannel != null)
                _redPwmChannel.DutyCycle = (double)red / 255.0;

            if (_greenPwmChannel != null)
                _greenPwmChannel.DutyCycle = (double)green / 255.0;

            _gpioController.Write(_pins.BluePin, blue ? PinValue.High : PinValue.Low);
        }
    }

    /*
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
    */
    
    public override void Dispose()
    {
        if (PlatformUtil.IsSupportedBoard())
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
            _gpioController.ClosePin(_pins.BluePin);
            //_gpioController.ClosePin(SpeakerPin);
            _gpioController.Dispose();
            base.Dispose();
        }
    }
}