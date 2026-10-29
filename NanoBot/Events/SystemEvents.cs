using NanoBot.Services;

namespace NanoBot.Events;

public class StartListeningEvent(object sender) : EventBase(sender);
public class StopListeningEvent(object sender) : EventBase(sender);

public class TalkLevelEvent(object sender, byte? level, bool skipLogging) : EventBase(sender, skipLogging)
{
    public byte? Level { get; } = level;    
}

public class ShutdownEvent(object sender) : EventBase(sender);

public class FunctionInvokingEvent(object sender) : EventBase(sender);
public class FunctionInvokedEvent(object sender) : EventBase(sender);

public class SystemErrorEvent(object sender) : EventBase(sender);
public class SystemOkEvent(object sender) : EventBase(sender);

public class WakeWordDetectedEvent(object sender) : EventBase(sender);

public class NoiseDetectedEvent(object sender) : EventBase(sender);
public class SilenceDetectedEvent(object sender) : EventBase(sender);

/// <summary>
/// Flash the LED in a colour for a while, overriding every other LED state until it ends.
/// Unset values take the defaults of <see cref="IGpioDeviceService.Blink"/> (red, one second).
/// </summary>
public class LedBlinkEvent(object sender, GpioDeviceLedColor? colour, TimeSpan? duration) : EventBase(sender)
{
    // Defaults only; IEventBus.Publish<TEvent>(sender) needs a constructor taking just the sender
    public LedBlinkEvent(object sender) : this(sender, null, null) { }

    public GpioDeviceLedColor? Colour { get; } = colour;
    public TimeSpan? Duration { get; } = duration;
}

public class ConfigChangedEvent(object sender) : EventBase(sender);

public class NightModeActivatedEvent(object sender) : EventBase(sender);
public class NightModeDeactivatedEvent(object sender) : EventBase(sender);