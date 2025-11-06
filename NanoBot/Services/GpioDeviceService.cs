using NanoBot.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Device.Gpio;
using NanoBot.Util;

namespace NanoBot.Services;

public enum GpioDeviceLedColor
{
    Off,
    Red,
    Green,
    Blue,
    Yellow,
    Cyan,
    Magenta,
    White
}

public interface IGpioDeviceService : IHostedService
{
    public GpioDeviceLedColor DefaultLedColour { get; set; }
}

public class GpioDeviceService : BackgroundService, IGpioDeviceService
{
    public GpioDeviceLedColor DefaultLedColour { get; set; } = GpioDeviceLedColor.White;

    private const int RedPin = 16;  // GPIO16 (Physical Pin 36)
    private const int GreenPin = 20; // GPIO20 (Physical Pin 38)
    private const int BluePin = 21; // GPIO21 (Physical Pin 40)
    private const int ButtonPin = 26; // GPIO26 (Pin 37 on the header)
    private const int SpeakerPin = 19; // GPIO19 (Physical Pin 35)

    private readonly ILogger _logger;
    private readonly IEventBus _bus;
    private readonly GpioController _gpioController;

    private bool _buttonPressed;
    private bool _isShutdown, _isListening, _isThinking, _isTalking, _isFunctionInvoking, _isWakeWordDetected, _isError;

    public GpioDeviceService(ILogger<GpioDeviceService> logger, IEventBus bus)
    {
        _logger = logger;
        _bus = bus;

        if (PlatformUtil.IsRaspberryPi())
        {
            _gpioController = new GpioController();

            _gpioController.OpenPin(RedPin, PinMode.Output);
            _gpioController.OpenPin(GreenPin, PinMode.Output);
            _gpioController.OpenPin(BluePin, PinMode.Output);
            _gpioController.OpenPin(SpeakerPin, PinMode.Output);
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

        _bus.Subscribe<StartTalkingEvent>(e => { ResetTransientStates(); _isTalking = true; SetSpeaker(true); UpdateLed(); });
        _bus.Subscribe<StopTalkingEvent>(e => { ResetTransientStates(); _isTalking = false; SetSpeaker(false); UpdateLed(); });

        _bus.Subscribe<StartThinkingEvent>(e => { ResetTransientStates(); _isThinking = true; UpdateLed(); });
        _bus.Subscribe<StopThinkingEvent>(e => { ResetTransientStates(); _isThinking = false; UpdateLed(); });

        _bus.Subscribe<FunctionInvokingEvent>(e => { ResetTransientStates(); _isFunctionInvoking = true; UpdateLed(); });
        _bus.Subscribe<FunctionInvokedEvent>(e => { ResetTransientStates(); _isFunctionInvoking = false; UpdateLed(); });

        _bus.Subscribe<WakeWordDetectedEvent>(e => { ResetTransientStates(); _isWakeWordDetected = true; UpdateLed(); });
    }

    private void ResetTransientStates()
    {
        _isWakeWordDetected = false;
    }

    private void UpdateLed()
    {
        if (_isShutdown)        
            SetLedColor(GpioDeviceLedColor.Off);
        else if (_isError)
            SetLedColor(GpioDeviceLedColor.Red);
        else if (_isListening)
            SetLedColor(GpioDeviceLedColor.Green);
        else if (_isTalking)
            SetLedColor(GpioDeviceLedColor.Magenta);            
        else if (_isFunctionInvoking)
            SetLedColor(GpioDeviceLedColor.Blue);
        else if (_isThinking)
            SetLedColor(GpioDeviceLedColor.Cyan);
        else if (_isWakeWordDetected)
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

    private void SetLedColor(GpioDeviceLedColor color)
    {
        _logger.LogDebug($"{nameof(SetLedColor)}: {@color}");

        PinValue red = PinValue.Low;
        PinValue green = PinValue.Low;
        PinValue blue = PinValue.Low;

        switch (color)
        {
            case GpioDeviceLedColor.Red:
                red = PinValue.High;
                break;
            case GpioDeviceLedColor.Green:
                green = PinValue.High;
                break;
            case GpioDeviceLedColor.Blue:
                blue = PinValue.High;
                break;
            case GpioDeviceLedColor.Yellow:
                red = PinValue.High;
                green = PinValue.High;
                break;
            case GpioDeviceLedColor.Cyan:
                green = PinValue.High;
                blue = PinValue.High;
                break;
            case GpioDeviceLedColor.Magenta:
                red = PinValue.High;
                blue = PinValue.High;
                break;
            case GpioDeviceLedColor.White:
                red = PinValue.High;
                green = PinValue.High;
                blue = PinValue.High;
                break;
            case GpioDeviceLedColor.Off:
                break;
            default:
                break;
        }

        if (PlatformUtil.IsRaspberryPi())
        {
            _gpioController.Write(RedPin, red);
            _gpioController.Write(GreenPin, green);
            _gpioController.Write(BluePin, blue);
        }
    }

    private void SetSpeaker(bool enabled)
    {
        _logger.LogDebug($"{nameof(SetSpeaker)}: {enabled}");

        if (PlatformUtil.IsRaspberryPi())
        {
            _gpioController.Write(SpeakerPin, enabled ? PinValue.High : PinValue.Low);
        }
    }

    public override void Dispose()
    {
        if (PlatformUtil.IsRaspberryPi())
        {
            // Clean up resources
            _gpioController.ClosePin(RedPin);
            _gpioController.ClosePin(GreenPin);
            _gpioController.ClosePin(BluePin);
            _gpioController.ClosePin(SpeakerPin);
            _gpioController.Dispose();
            base.Dispose();
        }
    }
}