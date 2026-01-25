using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NanoBot.Configuration;
using NanoBot.Events;
using NanoBot.Util;

namespace NanoBot.Services;

public interface ISystemService : IHostedService
{
    Task NotifyConversationEnd();
}

public class SystemService : BackgroundService, ISystemService
{
    private readonly ILogger<SystemService> _logger;
    private readonly IWakeWordService _wakeWordService;
    private readonly IOptionsMonitor<AppConfig> _appConfigMonitor;   
    private readonly IEventBus _bus;
    private readonly IAlsaControllerService _alsaControllerService;
    private readonly IHostApplicationLifetime _applicationLifetime;    
    private CancellationTokenSource _hangupCancellationTokenSource;
    private readonly object _hangupCancellationTokenLock = new();
    private readonly Func<AgentConfig, RealtimeAgent> _realtimeAgentFactory;
    private RealtimeAgent _realtimeAgent;
    private DateTime? _realtimeAgentCreatedAt;
    
    public SystemService(
        ILogger<SystemService> logger,
        IOptionsMonitor<AppConfig> appConfigMonitor, 
        IWakeWordService wakeWordService,
        IEventBus bus,
        IAlsaControllerService alsaControllerService,
        IHostApplicationLifetime applicationLifetime,
        Func<AgentConfig, RealtimeAgent> realtimeAgentFactory)
    {
        _logger = logger;
        _appConfigMonitor = appConfigMonitor;
        _wakeWordService = wakeWordService;
        _bus = bus;
        _alsaControllerService = alsaControllerService;
        _applicationLifetime = applicationLifetime;
        _realtimeAgentFactory = realtimeAgentFactory;

        WireUpEventHandlers();
    }    

    private void WireUpEventHandlers()
    {
        _bus.Subscribe<HangupInputEvent>(e => {
            _logger.LogDebug($"Received {e.GetType().Name}");
            CancelHangupToken();
        });

        _bus.Subscribe<VolumeCtrlUpInputEvent>(e => {
            _logger.LogDebug($"Received {e.GetType().Name}");
            _alsaControllerService.VolumeUp();
        });

        _bus.Subscribe<VolumeCtrlDownInputEvent>(e => {
            _logger.LogDebug($"Received {e.GetType().Name}");
            _alsaControllerService.VolumeDown();
        });

        _bus.Subscribe<ConfigChangedEvent>(e => {
            _logger.LogDebug($"Received {e.GetType().Name}");
            // TODO: Rebuild RealtimeAgent

            _realtimeAgent?.Dispose();
            _realtimeAgent = null;
            _realtimeAgentCreatedAt = null;
        });
    }

    private bool IsRealtimeAgentSessionExpired()
    {
        if (_realtimeAgent == null || _realtimeAgentCreatedAt == null)
            return false;

        var sessionTimeoutMinutes = _appConfigMonitor.CurrentValue.SessionTimeoutMinutes;
        if (sessionTimeoutMinutes <= 0)
            return false;

        var elapsed = DateTime.UtcNow - _realtimeAgentCreatedAt.Value;
        return elapsed.TotalMinutes >= sessionTimeoutMinutes;
    }

    private RealtimeAgent GetOrCreateRealtimeAgent(AgentConfig agentConfig)
    {
        // Check if existing agent has exceeded session timeout
        if (IsRealtimeAgentSessionExpired())
        {
            var elapsed = DateTime.UtcNow - _realtimeAgentCreatedAt!.Value;
            _logger.LogInformation($"Session timeout exceeded ({elapsed.TotalMinutes.ToString("F1")} minutes). Disposing and recreating realtime agent.");
            
            _realtimeAgent?.Dispose();
            _realtimeAgent = null;
            _realtimeAgentCreatedAt = null;
        }

        // Create new agent if needed
        if (_realtimeAgent == null)
        {
            _realtimeAgent = _realtimeAgentFactory(agentConfig);
            _realtimeAgentCreatedAt = DateTime.UtcNow;
            _logger.LogDebug($"Created new realtime agent at {_realtimeAgentCreatedAt.Value.ToString("O")}");
        }

        return _realtimeAgent;
    }        

    protected override Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var appConfig = _appConfigMonitor.CurrentValue;

        // Set initial playback volume from config
        if (appConfig.PlaybackVolume >= 0 && appConfig.PlaybackVolume <= 10)
        {
            _alsaControllerService.SetPlaybackVolume(appConfig.PlaybackVolume);
        }
        else
        {
            _logger.LogWarning($"Invalid PlaybackVolume value: {appConfig.PlaybackVolume}. Must be between 0 and 10.");
        }

        // Enable keyboard hangup listener only when not in console debug mode
        if (appConfig.ConsoleDebugMode)
        {
            // Fire-and-forget keyboard listener; do not block ExecuteAsync
            _ = StartKeyboardSpacebarListener(cancellationToken);
        }

        return Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {                    
                    _bus.Publish<SystemOkEvent>(this);

                    // Wait for wake word
                    var wakeWord = await WaitForWakeWord(cancellationToken);

                    // Transient notification that we got out of wake word waiting 
                    _bus.Publish<WakeWordDetectedEvent>(this);

                    // Retrieve agent associated to wake word
                    var appConfig = _appConfigMonitor.CurrentValue;
                    var agentConfig = appConfig.Agents?.FirstOrDefault(t =>
                        !t.Disabled && (wakeWord == null || string.Equals(t.WakeWord, wakeWord, StringComparison.OrdinalIgnoreCase)));
                    if (agentConfig == null)
                    {
                        _logger.LogError($"Could not establish agent associated to wake word: {wakeWord}");
                        return;
                    }

                    _logger.LogDebug($"Established agent: {agentConfig.Name}");

                    var agent = GetOrCreateRealtimeAgent(agentConfig);

                    var hangupToken = GetOrCreateHangupToken(cancellationToken);

                    try
                    {
                        var runResult = await agent.RunAsync(() => { _bus.Publish<StartListeningEvent>(this); }, hangupToken);
                    }
                    finally
                    {
                        _bus.Publish<StopListeningEvent>(this);
                    }
                    
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception m)
                {
                    _bus.Publish<SystemErrorEvent>(this);

                    _logger.LogError(m, m.Message);

                    _realtimeAgent?.Dispose();
                    _realtimeAgent = null;
                    _realtimeAgentCreatedAt = null;

                    await Task.Delay(5000, cancellationToken);
                }
            }
        }, cancellationToken);
    }

    public async Task<string> WaitForWakeWord(CancellationToken cancellationToken)
    {
        // Wait for wake word
        var hangupToken = GetOrCreateHangupToken(cancellationToken);
        try
        {
            var wakeWord = await _wakeWordService.WaitForWakeWordAsync(hangupToken);
            if (wakeWord == null) // Got cancelled
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning($"{nameof(_wakeWordService.WaitForWakeWordAsync)} cancelled.");
                    return null;
                }

                _logger.LogWarning($"{nameof(_wakeWordService.WaitForWakeWordAsync)} cancelled due to hangup event.");
                return null;
            }
            else // Got wake word
            {
                _logger.LogDebug($"Got wake word: {wakeWord}");
                return wakeWord;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning($"{nameof(_wakeWordService.WaitForWakeWordAsync)} cancelled.");
            return null;
        }
    }
    private Task StartKeyboardSpacebarListener(CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Skip if no console or input is redirected (e.g., service/daemon)
                    if (!Console.IsInputRedirected && Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.Spacebar)
                        {
                            _logger.LogDebug("Spacebar pressed -> publishing HangupInputEvent.");
                            _bus.Publish<HangupInputEvent>(this);
                        }
                    }

                    await Task.Delay(50, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // normal on shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Keyboard listener failed.");
            }
        }, cancellationToken);
    }

    public async Task NotifyConversationEnd()
    {
        CancelHangupToken();
    }
    private CancellationToken GetOrCreateHangupToken(CancellationToken baseToken)
    {
        lock (_hangupCancellationTokenLock)
        {
            // If current token is cancelled or doesn't exist, create new one
            if (_hangupCancellationTokenSource == null || _hangupCancellationTokenSource.Token.IsCancellationRequested)
            {
                _hangupCancellationTokenSource?.Dispose();
                _hangupCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(baseToken);
            }
            return _hangupCancellationTokenSource.Token;
        }
    }

    private void CancelHangupToken()
    {
        lock (_hangupCancellationTokenLock)
        {
            _hangupCancellationTokenSource?.Cancel();
        }
    }
}
