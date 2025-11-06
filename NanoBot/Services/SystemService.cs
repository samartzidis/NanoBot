using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using NanoBot.Configuration;
using NanoBot.Events;
using NanoBot.Util;
using System.Runtime.CompilerServices;
using System.Text;

namespace NanoBot.Services;

public interface ISystemService : IHostedService
{
    public ChatHistory History { get; }
    public void ShellExecute(string cmd, string pars);
    public void StopApplication();
}

public class SystemService : BackgroundService, ISystemService
{
    private const string FollowResponseMarker = "[FOLLOW]";

    private readonly ILogger<SystemService> _logger;
    private readonly IVoiceService _voiceService;
    private readonly IAgentFactoryService _agentFactoryService;
    private readonly IDynamicOptions<AppConfig> _appConfigOptions;   
    private readonly IEventBus _bus;
    private readonly IAlsaControllerService _alsaControllerService;
    private readonly IHostApplicationLifetime _applicationLifetime;    

    private CancellationTokenSource _hangupCancellationTokenSource;
    private readonly object _hangupCancellationTokenLock = new object();
    private DateTime _lastConversationTimestamp;
    private readonly ChatHistory _history;

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

    public ChatHistory History => _history;

    public SystemService(
        ILogger<SystemService> logger,
        IDynamicOptions<AppConfig> appConfigOptions, 
        IVoiceService voiceService, 
        IAgentFactoryService agentFactoryService, 
        IEventBus bus,
        IAlsaControllerService alsaControllerService,
        IHostApplicationLifetime applicationLifetime)
    {
        _logger = logger;
        _appConfigOptions = appConfigOptions;
        _voiceService = voiceService;
        _agentFactoryService = agentFactoryService;
        _history = new ChatHistory();
        _bus = bus;
        _alsaControllerService = alsaControllerService;
        _applicationLifetime = applicationLifetime;

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
    }        

    protected override Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var appConfig = _appConfigOptions.Value;

        return Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {                    
                    _bus.Publish<SystemOkEvent>(this);
                    
                    if (!appConfig.ConsoleDebug)
                        await ConversationLoop(appConfig, cancellationToken);
                    else
                        await ConsoleDebugConversationLoop(appConfig,cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception m)
                {
                    _bus.Publish<SystemErrorEvent>(this);

                    _logger.LogError(m, m.Message);
                    
                    await Task.Delay(5000, cancellationToken);
                }
            }
        }, cancellationToken);
    }
    
    private async Task ConsoleDebugConversationLoop(AppConfig appConfig, CancellationToken cancellationToken)
    {
        var agentConfig = appConfig.Agents?.FirstOrDefault(a => !a.Disabled);
        if (agentConfig == null)
            throw new Exception("No enabled agents found in config.");

        // Create agent for this conversation
        var currentAgent = await _agentFactoryService.CreateAgentAsync(agentConfig.Name, kb =>
        {
            kb.Services.AddSingleton<ISystemService>(this);
            kb.Services.AddSingleton(agentConfig);
        }, cancellationToken: cancellationToken);

        if (currentAgent == null)
            throw new Exception($"Failed to create agent: {agentConfig.Name}");

        Console.WriteLine($"[Console mode] Agent: {agentConfig.Name}");
        Console.WriteLine("Press Enter to rebuild agent.");

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("You> ");
            var line = Console.ReadLine();
            if (line == null)
                break;

            // If user presses Enter (empty line), rebuild the agent
            if (string.IsNullOrWhiteSpace(line))
            {
                Console.WriteLine("Rebuilding agent...");
                return;
            }

            var sb = new StringBuilder();
            await foreach (var msg in InvokeAgentAsync(currentAgent, _history, line, cancellationToken))
                sb.Append(msg);

            var response = sb.ToString();            
            Console.WriteLine($"Agent> {response}");
        }
    }
    
    
    private async Task ConversationLoop(AppConfig appConfig, CancellationToken cancellationToken)
    {
        // Update the last conversation timestamp
        _lastConversationTimestamp = DateTime.Now;

        // Wait for wake word
        var wakeWord = await WaitForWakeWord(cancellationToken);
       
        // Transient notification that we got out of wake word waiting 
        _bus.Publish<WakeWordDetectedEvent>(this);

		// Retrieve agent associated to wake word
		AgentConfig agentConfig = null;
		AgentConfig newAgentConfig;
		if (wakeWord == null)
		{
			newAgentConfig = appConfig.Agents?.FirstOrDefault(t => !t.Disabled);
		}
		else
		{
			newAgentConfig = appConfig.Agents?.FirstOrDefault(t => !t.Disabled && string.Equals(t.WakeWord, wakeWord, StringComparison.OrdinalIgnoreCase));
		}

		agentConfig = newAgentConfig;

        if (agentConfig == null)
        {
            _logger.LogError($"Could not establish agent associated to wake word: {wakeWord}");
            return;
        }

        _logger.LogDebug($"Established agent: {agentConfig.Name}");

        if (appConfig.ChatHistoryTimeToLiveMinutes > 0)
        {
            // If any current history is too old, clear it
            var lastConversationTimeSpan = DateTime.Now - _lastConversationTimestamp;
            _logger.LogDebug($"Last conversation was {@lastConversationTimeSpan} ago.");
            if (lastConversationTimeSpan > TimeSpan.FromMinutes(appConfig.ChatHistoryTimeToLiveMinutes))
            {
                _logger.LogDebug("Clearing history.");
                _history.Clear();
            }
        }

        // Instantiate agent - inject dynamic dependencies
        var agent = await _agentFactoryService.CreateAgentAsync(agentConfig.Name, kernelBuilder => {
            kernelBuilder.Services.AddSingleton<ISystemService>(this);
            kernelBuilder.Services.AddSingleton<AgentConfig>(agentConfig);
        }, cancellationToken: cancellationToken);

        if (agent == null)
        {
            _logger.LogError($"Failed to instantiate agent: {agentConfig.Name}");
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {            
            byte[] userAudioBuffer;
            var hangupToken = GetOrCreateHangupToken(cancellationToken);
            try
            {                                           
                _bus.Publish<StartListeningEvent>(this);

                // Wait for user speech message
                var res = _voiceService.WaitForSpeech(out userAudioBuffer, cancellationToken: hangupToken);
                if (res != ReceiveVoiceMessageResult.Ok)
                {
                    _logger.LogWarning($"{nameof(_voiceService.WaitForSpeech)} failed: {res}");

                    return; // Complete the conversation loop
                }
            }
            finally
            {                    
                _bus.Publish<StopListeningEvent>(this);
            }
                
            // Pass hangupToken to enable cancellation during thinking phase
            var agentMessage = await TranscribeAndThink(appConfig, agentConfig, userAudioBuffer, agent, hangupToken);
            if (agentMessage == null)
                return;

            _logger.LogDebug($"Agent: {agentMessage}");

            // Check for FollowResponseMarker and strip it out
            var followMarker = agentMessage.LastIndexOf(FollowResponseMarker, StringComparison.OrdinalIgnoreCase);
            if (followMarker != -1)
                agentMessage = agentMessage.Remove(followMarker, FollowResponseMarker.Length);

            await VoiceReply(agentConfig, agentMessage, cancellationToken);

            // If speech was cancelled (hangup), exit conversation loop to return to wake word waiting
            if (hangupToken.IsCancellationRequested)
            {
                _logger.LogDebug("Conversation cancelled during speech, returning to wake word waiting.");
                return; // Complete the conversation loop
            }

            // Heuristically detect if the agent message was a question (only works for English)
            if (agentMessage.Contains("?"))
                continue; // Continue the conversation loop

            // If no FollowResponseMarker (AI detection of question content), complete the conversation.
            if (followMarker < 0)
                return; // Complete the conversation loop
        }
    }
    
    public async Task<string> WaitForWakeWord(CancellationToken cancellationToken)
    {
        // Wait for wake word
        var hangupToken = GetOrCreateHangupToken(cancellationToken);
        try
        {
            var wakeWord = await _voiceService.WaitForWakeWordAsync(null, hangupToken);
            if (wakeWord == null) // Got cancelled
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning($"{nameof(_voiceService.WaitForWakeWordAsync)} cancelled.");
                    return null;
                }

                _logger.LogWarning($"{nameof(_voiceService.WaitForWakeWordAsync)} cancelled due to hangup event.");
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
            _logger.LogWarning($"{nameof(_voiceService.WaitForWakeWordAsync)} cancelled.");
            return null;
        }
    }

    public async Task<string> TranscribeAndThink(AppConfig appConfig, AgentConfig agentConfig, byte[] userAudioBuffer, ChatCompletionAgent agent, CancellationToken cancellationToken)
    {
        try
        {
            _bus.Publish<StartThinkingEvent>(this);

            var audioTranscriptionLanguage = "en";
            if (appConfig.VoiceService.TextToSpeechServiceProvider == TextToSpeechServiceProviderConfig.AzureSpeechService)
            {

                var idx = agentConfig.SpeechSynthesisVoiceName.IndexOf('-');
                if (idx < 0)
                {
                    _logger.LogError($"Invalid {nameof(AgentConfig.SpeechSynthesisVoiceName)} string format: {agentConfig.SpeechSynthesisVoiceName}");
                    return null; // Complete the conversation loop
                }

                audioTranscriptionLanguage = agentConfig.SpeechSynthesisVoiceName.Substring(0, idx);

            }
            _logger.LogDebug($"Using AudioTranscriptionLanguage: {audioTranscriptionLanguage}");

            // Validate audio buffer is not empty or too short before attempting transcription
            // Minimum audio length is 0.1 seconds, assuming 16kHz sample rate and 16-bit samples:
            // 0.1 seconds * 16000 samples/sec * 2 bytes/sample = 3200 bytes minimum for PCM data
            // Plus WAV header (44 bytes), minimum would be around 3244 bytes total
            // But we'll use a more conservative check - if buffer is less than ~4KB, it's likely too short
            if (userAudioBuffer == null || userAudioBuffer.Length < 4000)
            {
                _logger.LogWarning($"Audio buffer too short ({userAudioBuffer?.Length ?? 0} bytes), skipping transcription.");
                return null; // Complete the conversation loop
            }

            // Transcribe user speech message
            var userMessage = _voiceService.GenerateSpeechToText(userAudioBuffer, audioTranscriptionLanguage);
            _logger.LogDebug($"User: {userMessage}");

            // Ignore blank transcribed message (e.g. noise)
            if (string.IsNullOrEmpty(userMessage))
            {
                _logger.LogWarning("Ignoring blank transcribed user message (noise?).");
                return null; // Complete the conversation loop
            }

            // Check for cancellation before starting LLM call
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning($"{nameof(TranscribeAndThink)} cancelled before LLM invocation.");
                return null; // Complete the conversation loop
            }

            // Stop on receiving custom "stop" message
            //if (IsStopWord(agentConfig, userMessage))
            //{
            //    _logger.LogDebug("Received stop message.");
            //    return null; // Complete the conversation loop
            //}

            var agentMessageBuilder = new StringBuilder();
            try
            {
                await foreach (var message in InvokeAgentAsync(agent, _history, userMessage, cancellationToken: cancellationToken))
                {
                    agentMessageBuilder.AppendLine(message);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning($"{nameof(TranscribeAndThink)} cancelled during LLM invocation.");
                return null; // Complete the conversation loop
            }
            var agentMessage = agentMessageBuilder.ToString();

            return agentMessage;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning($"{nameof(TranscribeAndThink)} cancelled.");
            return null; // Complete the conversation loop
        }
        finally
        {
            _bus.Publish<StopThinkingEvent>(this);
        }
    }

    public async Task VoiceReply(AgentConfig agentConfig, string agentMessage, CancellationToken cancellationToken)
    {
        // Speak back to user BUT allow interruption
        var hangupToken = GetOrCreateHangupToken(cancellationToken);
        try
        {
            _bus.Publish<StartTalkingEvent>(this);

            var wakeTask = _voiceService.WaitForWakeWordAsync(t => {
                CancelHangupToken();
            }, hangupToken);

            var speakTask = _voiceService.GenerateTextToSpeechAsync(agentMessage, agentConfig.SpeechSynthesisVoiceName, hangupToken);

            var completedTask = await Task.WhenAny(wakeTask, speakTask);
            if (completedTask == speakTask)
                _logger.LogDebug("Speech complete.");
            else if (completedTask == wakeTask)
                _logger.LogDebug("Speech interrupted by wake word.");
            else
                _logger.LogDebug("Speech interrupted.");
        }
        finally
        {
            _bus.Publish<StopTalkingEvent>(this);
        }
    }


    private async IAsyncEnumerable<string> InvokeAgentAsync(ChatCompletionAgent agent, ChatHistory history, string userMessage, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Invoking LLM...");

            var contentItemCollection = new ChatMessageContentItemCollection { new TextContent(userMessage) };
            history.Add(new ChatMessageContent(AuthorRole.User, contentItemCollection));

            await foreach (var responseMessage in agent.InvokeAsync(history, null, null, cancellationToken))
            {
                if (responseMessage.Message.Role == AuthorRole.Assistant)
                    yield return responseMessage.Message.Content;

                history.Add(responseMessage);
            }

            var res = await agent.ReduceAsync(history, cancellationToken);
            if (res)
                _logger.LogDebug("History truncated.");
        }
        finally
        {
            _logger.LogDebug("Invoking LLM complete.");
        }
    }

    //private bool IsStopWord(AgentConfig agentConfig, string input)
    //{
    //    // Remove all non-letter characters
    //    input = Regex.Replace(input, @"[^\p{L}]", string.Empty);
    //    input = RemoveDiacritics(input);
    //    var stopWord = RemoveDiacritics(agentConfig.StopWord);

    //    _logger.LogDebug($"Checking input '{input}' for stop word '{stopWord}'");
            
    //    return string.Equals(input, stopWord, StringComparison.InvariantCultureIgnoreCase);
    //}

    //private string RemoveDiacritics(string text)
    //{
    //    if (string.IsNullOrEmpty(text)) 
    //        return text;

    //    // Decompose to base characters and diacritics
    //    var normalized = text.Normalize(NormalizationForm.FormD);

    //    // Filter out non-spacing marks (accents)
    //    var result = new StringBuilder();
    //    foreach (var c in normalized)
    //    {
    //        if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
    //            result.Append(c);
    //    }

    //    return result.ToString().Normalize(NormalizationForm.FormC);
    //}

    public void StopApplication()
    {
        _applicationLifetime.StopApplication();
    }

    public async void ShellExecute(string cmd, string pars)
    {
        if (PlatformUtil.IsRaspberryPi())
        {
            try
            {
                // Create a new process to execute the shutdown command
                var processStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = cmd,
                    Arguments = pars,
                    UseShellExecute = false,  // Ensure the process is started without a shell
                    RedirectStandardOutput = false,  // Capture output (optional)
                    RedirectStandardError = false  // Capture error output (optional)
                };

                // Start the process
                using var process = System.Diagnostics.Process.Start(processStartInfo);
                if (process != null)
                    await process.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error: {ex.Message}");
            }
        }
    }
}