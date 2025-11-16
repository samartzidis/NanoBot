using NanoBot.Services;

namespace NanoBot.Events;

public class StartListeningEvent(object sender) : EventBase(sender);
public class StopListeningEvent(object sender) : EventBase(sender);

public class StartTalkingEvent(object sender) : EventBase(sender);
public class StopTalkingEvent(object sender) : EventBase(sender);

public class StartThinkingEvent(object sender) : EventBase(sender);
public class StopThinkingEvent(object sender) : EventBase(sender);

public class ShutdownEvent(object sender) : EventBase(sender);

public class FunctionInvokingEvent(object sender) : EventBase(sender);
public class FunctionInvokedEvent(object sender) : EventBase(sender);

public class SystemErrorEvent(object sender) : EventBase(sender);
public class SystemOkEvent(object sender) : EventBase(sender);

public class WakeWordDetectedEvent(object sender) : EventBase(sender);

public class NoiseDetectedEvent(object sender) : EventBase(sender);
public class SilenceDetectedEvent(object sender) : EventBase(sender);
