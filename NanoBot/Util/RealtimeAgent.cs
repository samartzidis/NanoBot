using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NanoBot.Events;
using NanoBot.Services;
using OpenAI.Realtime;
using Pv;
using System.ClientModel;
using System.Text;

namespace NanoBot.Util;

public sealed class RealtimeAgentOptions
{
    public string Model { get; set; }
    public string Voice { get; set; }
    public string Instructions { get; set; }
    public string OpenAiApiKey { get; set; }
    public string OpenAiEndpoint { get; set; }
    public float? Temperature { get; set; }
    public float SpeechSpeed { get; set; } = 1.0f;
    public int? ConversationInactivityTimeoutSeconds { get; set; }    
}

public enum RealtimeAgentRunResult
{
    Cancelled,
    InactivityTimeout
}

public enum StateUpdate
{
    Ready,
    SpeakingStarted,
    SpeakingStopped
}

/// <summary>
/// Manages a realtime conversation session with OpenAI using local VAD (Voice Activity Detection).
/// The session persists across multiple RunAsync calls until the object is disposed.
/// </summary>
public sealed class RealtimeAgent : IDisposable
{
    // Audio configuration - must match Realtime API requirements
    private const int SampleRate = 24000; // Realtime API uses 24kHz
    private const int VadSampleRate = 16000; // Silero VAD uses 16kHz
    private const int FrameLength = 512; // Frame size for VAD (matches 16kHz requirement)
    private const float VadThreshold = 0.5f;
    private const int MinSpeechFrames = 3; // Minimum consecutive speech frames to start recording
    private const int MinSpeechFramesForBargeIn = 2; // Fewer frames needed for barge-in (faster response)
    private const int SilenceFramesToStop = 50; // ~1.6 seconds of silence to stop recording
    private const int PreBufferFrames = 15; // Keep ~0.5s of audio before speech is detected

    private readonly IReadOnlyList<AIFunction> _tools;
    private readonly ILogger _logger;
    private readonly IEventBus _bus;
    private readonly RealtimeAgentOptions _options;
    private readonly object _speakerLock = new();
    private readonly Dictionary<string, StringBuilder> _functionArgumentBuildersById = new();

    private RealtimeClient _realtimeClient;
    private RealtimeSessionClient _session;
    private bool _disposed;

    // Receive task runs for the lifetime of the session (not per RunAsync call)
    private Task _receiveTask;
    // Session CTS is independent - only cancelled on dispose, not on RunAsync cancellation
    private CancellationTokenSource _sessionCts;

    // Shared state between receive task and audio loop. _currentSpeaker is where receive-side
    // audio is written to; null during audio-device transitions and outside of RunAsync.
    private Speaker _currentSpeaker;

    // True between the start of RunAsync's audio-capture loop and the finally that tears it down.
    // Used by FaultSession to distinguish a real user-visible failure (true => publish
    // SystemErrorEvent for red eyes) from a background session ending while we're idle waiting
    // for the next wake word (false => suppress the event so the eyes don't get stuck red).
    // Set/cleared inside _speakerLock to keep its publication ordered with _currentSpeaker.
    private volatile bool _runActive;
    private volatile bool _modelIsSpeaking;
    private volatile bool _waitingForResponse;        
    private volatile bool _bargeInTriggered;
    private volatile bool _responseActive;
    
    private volatile Action<StateUpdate> _stateUpdateAction;
    private DateTime _responseRequestedAtUtc;

    // Long-lived audio devices (created once, reused across RunAsync calls)
    private PvRecorder _recorder;
    private Speaker _speaker;
    private SileroVadDetector _vadDetector;

    // Barge-in tracking for truncation
    private readonly object _outputAudioLock = new();
    private string _currentStreamingItemId;
    private int _audioBytesSentToSpeaker;

    public RealtimeAgent(ILogger<RealtimeAgent> logger, IReadOnlyList<AIFunction> tools, IEventBus bus, IOptions<RealtimeAgentOptions> options)
    {
        _logger = logger;
        _tools = tools;
        _bus = bus;
        _options = options.Value;
    }


    /// <summary>
    /// Runs the conversation loop until cancellation is requested or VAD inactivity timeout occurs.
    /// Both cancellation and timeout preserve the session - call DisposeAsync to close the session.
    /// </summary>
    /// <returns>Why the loop returned.</returns>
    public async Task<RealtimeAgentRunResult> RunAsync(
        Action<StateUpdate> stateUpdateAction = null, 
        Action<byte> meterAction = null, 
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Get the Realtime client
        _realtimeClient ??= GetRealtimeConversationClient();

        // Lazily create audio devices (reused across RunAsync calls)
        _recorder ??= PvRecorder.Create(frameLength: FrameLength, deviceIndex: -1);
        if (_speaker is null)
            _speaker = new Speaker(sampleRate: SampleRate, bitsPerSample: 16, meterAction: meterAction);
        else
            _speaker.MeterAction = meterAction;
        _vadDetector ??= new SileroVadDetector(VadSampleRate);

        // Start a new conversation session
        if (_session is null)
        {
            _logger.LogDebug("Connecting to OpenAI Realtime API...");

            // Session CTS is independent - not linked to the RunAsync cancellation token
            _sessionCts = new CancellationTokenSource();
            _session = await _realtimeClient.StartConversationSessionAsync(
                _options.Model,
                cancellationToken: cancellationToken);

            // Configure session with LOCAL VAD (server VAD disabled)
            await ConfigureSessionAsync(_session, cancellationToken);

            _logger.LogDebug("Session configured.");
        }

        // Set current speaker for receive task to use, and mark the run as active so
        // FaultSession knows a user-facing call is in progress.
        lock (_speakerLock)
        {
            _currentSpeaker = _speaker;
            _runActive = true;
        }

        _logger.LogDebug($"Using microphone: {_recorder.SelectedDevice}");

        // Check if the receive task completed unexpectedly (WebSocket closed)
        // If so, the session is dead and we need to reconnect
        if (_receiveTask is not null && _receiveTask.IsCompleted)
        {
            _logger.LogDebug("[Session connection lost - reconnecting...]");
            await ResetSessionAsync();

            // Recreate session
            _sessionCts = new CancellationTokenSource();
            _session = await _realtimeClient.StartConversationSessionAsync(
                _options.Model,
                cancellationToken: cancellationToken);

            // Reconfigure session
            await ConfigureSessionAsync(_session, cancellationToken);
        }

        // Start the receive task if not already running
        if (_receiveTask is null || _receiveTask.IsCompleted)
            _receiveTask = RunReceiveTaskAsync(_session, _sessionCts!.Token);

        _stateUpdateAction = stateUpdateAction;
        _waitingForResponse = false;
        stateUpdateAction?.Invoke(StateUpdate.Ready);

        // Start the conversation loop (audio capture). Link with the session CTS so that a
        // receive-side error (which calls FaultSession -> cancels _sessionCts) immediately tears
        // down audio capture instead of leaving it stuck waiting for a response that will never
        // come.
        RealtimeAgentRunResult result;
        try
        {
            using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessionCts!.Token);
            result = await AudioCaptureLoopAsync(_session, _recorder, _speaker, _vadDetector, loopCts.Token);
        }
        finally
        {
            lock (_speakerLock)
            {
                _currentSpeaker = null;
                _runActive = false;
            }

            // Observe and log any receive-task fault. This serves two purposes:
            //   1. Prevents an unobserved task exception leak when the audio loop itself threw
            //      (in which case we never reach the `await _receiveTask` below).
            //   2. Surfaces the receive-side root cause - the receive task is usually the FIRST
            //      to see a server-side WebSocket close, while the audio loop only sees the
            //      second-order symptom (e.g. WebSocketException with State='Aborted'). Without
            //      this log we'd lose visibility into the original I/O failure.
            if (_receiveTask is { IsCompleted: true, IsFaulted: true })
            {
                var receiveError = _receiveTask.Exception?.GetBaseException();
                if (receiveError is not null)
                    _logger.LogWarning($"[Receive task faulted: {receiveError.GetType().Name}: {receiveError.Message}]");
                else
                    _ = _receiveTask.Exception; // belt-and-braces: still mark observed
            }
        }

        // If the receive task faulted (Realtime API error or unexpected stream end), rethrow its
        // exception so SystemService.ExecuteAsync's catch block disposes and recreates the agent
        // (and keeps the red-eye indication for the recovery delay). await unwraps the
        // AggregateException to the original exception.
        if (_receiveTask is { IsCompleted: true, IsFaulted: true })
            await _receiveTask;

        return result;
    }

    /// <summary>
    /// Resets the session state asynchronously, cleaning up the old session.
    /// </summary>
    private async Task ResetSessionAsync()
    {
        if (_sessionCts is not null)
            await _sessionCts.CancelAsync();

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask;
            }
            catch
            {
                // Ignore errors during cleanup
            }
        }

        try
        {
            _session?.Dispose();
        }
        catch
        {
            // Ignore errors during cleanup
        }

        _sessionCts?.Dispose();
        _session = null;
        _sessionCts = null;
        _receiveTask = null;
    }

    /// <summary>
    /// Disposes the conversation synchronously.
    /// Prefer DisposeAsync for graceful shutdown.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) 
            return;
        _disposed = true;

        // Cancel the session
        _sessionCts?.Cancel();

        // Don't wait for receive task in sync dispose - just cancel and move on

        _session?.Dispose();

        _sessionCts?.Dispose();

        // Dispose long-lived audio devices
        _vadDetector?.Dispose();
        _speaker?.Dispose();
        _recorder?.Dispose();

        _session = null;
        _sessionCts = null;
        _receiveTask = null;
        _vadDetector = null;
        _speaker = null;
        _recorder = null;
    }

    /// <summary>
    /// Receive task that runs for the lifetime of the session.
    /// Uses the session-level cancellation token, not the per-RunAsync token.
    /// </summary>
    private async Task RunReceiveTaskAsync(RealtimeSessionClient session, CancellationToken sessionToken)
    {
        var outputAudioBuffer = new List<byte>();
        const int SpeakerChunkSize = 16384;

        try
        {
            await foreach (RealtimeServerUpdate update in session.ReceiveUpdatesAsync(sessionToken))
            {
                if (sessionToken.IsCancellationRequested) break;

                // Session started
                if (update is RealtimeServerUpdateSessionCreated sessionCreatedUpdate)
                    _logger.LogDebug($"[Session started: {sessionCreatedUpdate.EventId}]");

                // Model started generating a response output item
                if (update is RealtimeServerUpdateResponseOutputItemAdded outputItemAdded)
                {
                    _waitingForResponse = false;
                    _modelIsSpeaking = true;
                    _responseActive = true;
                    _stateUpdateAction?.Invoke(StateUpdate.SpeakingStarted);
                    _bargeInTriggered = false;

                    var itemId = GetItemId(outputItemAdded.Item);
                    string functionName = (outputItemAdded.Item as RealtimeFunctionCallItem)?.FunctionName;

                    lock (_outputAudioLock)
                    {
                        outputAudioBuffer.Clear();
                        _currentStreamingItemId = itemId;
                        _audioBytesSentToSpeaker = 0;
                    }

                    _logger.LogDebug($"[OutputItemAdded: FunctionName={functionName ?? "null"}, ItemId={itemId ?? "null"}]");

                    if (!string.IsNullOrEmpty(functionName))
                        _logger.LogDebug($"[Calling: {functionName}] ");
                }

                // Audio transcript delta
                if (update is RealtimeServerUpdateResponseOutputAudioTranscriptDelta transcriptDelta)
                {
                    if (!string.IsNullOrEmpty(transcriptDelta.Delta))
                        _logger.LogDebug(transcriptDelta.Delta);
                }

                // Text delta
                if (update is RealtimeServerUpdateResponseOutputTextDelta textDelta)
                {
                    if (!string.IsNullOrEmpty(textDelta.Delta))
                        _logger.LogDebug($"[TextDelta: {textDelta.Delta}]");
                }

                // Audio bytes delta (use lock to synchronize with barge-in)
                if (update is RealtimeServerUpdateResponseOutputAudioDelta audioDelta)
                {
                    if (audioDelta.Delta is not null)
                    {
                        lock (_outputAudioLock)
                        {
                            if (!_bargeInTriggered)
                            {
                                var audioBytes = audioDelta.Delta.ToArray();
                                outputAudioBuffer.AddRange(audioBytes);

                                while (outputAudioBuffer.Count >= SpeakerChunkSize)
                                {
                                    byte[] chunk = new byte[SpeakerChunkSize];
                                    outputAudioBuffer.CopyTo(0, chunk, 0, SpeakerChunkSize);
                                    outputAudioBuffer.RemoveRange(0, SpeakerChunkSize);
                                    WriteSpeakerSafe(chunk);
                                    _audioBytesSentToSpeaker += SpeakerChunkSize;
                                }
                            }
                        }
                    }
                }

                // Function call arguments delta
                if (update is RealtimeServerUpdateResponseFunctionCallArgumentsDelta funcArgsDelta)
                {
                    var argsText = funcArgsDelta.Delta?.ToString();
                    if (!string.IsNullOrWhiteSpace(argsText))
                    {
                        if (!_functionArgumentBuildersById.TryGetValue(funcArgsDelta.ItemId, out var builder))
                        {
                            _functionArgumentBuildersById[funcArgsDelta.ItemId] = builder = new StringBuilder();
                        }
                        builder.Append(argsText);

                        _logger.LogDebug($"[FunctionArgsDelta: ItemId={funcArgsDelta.ItemId}, CallId={funcArgsDelta.CallId ?? "null"}, Args={argsText}]");
                    }
                }

                // Output item finished
                if (update is RealtimeServerUpdateResponseOutputItemDone outputItemDone)
                {
                    var funcCallItem = outputItemDone.Item as RealtimeFunctionCallItem;
                    var itemId = funcCallItem?.Id ?? (outputItemDone.Item as RealtimeMessageItem)?.Id;
                    var functionCallId = funcCallItem?.CallId;
                    var functionName = funcCallItem?.FunctionName;

                    _logger.LogDebug($"[OutputItemDone: FunctionCallId={functionCallId ?? "null"}, FunctionName={functionName ?? "null"}, ItemId={itemId ?? "null"}]");

                    if (functionCallId is not null)
                    {
                        if (_tools == null || _tools.Count == 0)
                        {
                            throw new InvalidOperationException(
                                $"Function '{functionName}' was called but no tools are registered.");
                        }

                        _logger.LogDebug($"[Executing function: {functionName}]");

                        RealtimeFunctionCallOutputItem functionOutputItem;
                        try
                        {
                            functionOutputItem = await ToolInvoker.InvokeFunctionAsync(
                                functionName,
                                functionCallId,
                                funcCallItem.Id,
                                _functionArgumentBuildersById,
                                _tools,
                                _logger,
                                _bus,
                                sessionToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"[Function '{functionName}' failed: {ex.Message}]");

                            functionOutputItem = RealtimeItem.CreateFunctionCallOutputItem(
                                callId: functionCallId,
                                functionOutput: $"Error: {ex.Message}");
                        }

                        await session.AddItemAsync(functionOutputItem, sessionToken);
                    }
                }

                // Input audio transcription completed (only if whisper transcription is enabled)
                if (update is RealtimeServerUpdateConversationItemInputAudioTranscriptionCompleted transcriptionUpdate)
                    _logger.LogDebug($"[You said: {transcriptionUpdate.Transcript}]");

                // Response finished
                if (update is RealtimeServerUpdateResponseDone responseDone)
                {
                    _waitingForResponse = false;
                    _responseActive = false;

                    // Write any remaining buffered audio
                    lock (_outputAudioLock)
                    {
                        if (outputAudioBuffer.Count > 0 && !_bargeInTriggered)
                        {
                            byte[] remainingChunk = new byte[outputAudioBuffer.Count];
                            outputAudioBuffer.CopyTo(remainingChunk, 0);
                            WriteSpeakerSafe(remainingChunk);
                            _audioBytesSentToSpeaker += remainingChunk.Length;
                            outputAudioBuffer.Clear();
                        }
                    }

                    // Wait for playback to finish (allows barge-in during playback since _modelIsSpeaking stays true)
                    await FlushSpeakerSafeAsync(sessionToken);
                    _modelIsSpeaking = false;
                    _stateUpdateAction?.Invoke(StateUpdate.SpeakingStopped);

                    var outputItems = responseDone.Response?.OutputItems ?? [];

                    _logger.LogDebug($"[ResponseDone: {outputItems.Count} items created]");

                    foreach (var item in outputItems)
                    {
                        if (item is RealtimeFunctionCallItem fc)
                            _logger.LogDebug($"  - FunctionCall: {fc.FunctionName}, CallId={fc.CallId}");
                        else if (item is RealtimeMessageItem msg)
                            _logger.LogDebug($"  - Message: Id={msg.Id}");
                        else
                            _logger.LogDebug($"  - Item: {item.GetType().Name}");
                    }

                    if (outputItems.OfType<RealtimeFunctionCallItem>().Any())
                    {
                        _logger.LogDebug("[Function calls detected - triggering response...]");
                        _waitingForResponse = true;
                        _responseActive = true;
                        _responseRequestedAtUtc = DateTime.UtcNow;
                        await session.StartResponseAsync(sessionToken);
                    }
                    else
                    {
                        _logger.LogDebug("[Ready for your next question...]");
                    }
                }

                // Explicit Realtime API error (quota exceeded, auth failure, malformed request,
                // etc.). This is always a real, user-visible failure - surface to the user (red
                // eyes via FaultSession) and throw so this task faults. RunAsync observes the
                // fault and rethrows, letting SystemService dispose + recreate the agent.
                if (update is RealtimeServerUpdateError errorUpdate)
                {
                    var msg = errorUpdate.Error?.Message ?? "Unknown Realtime API error";
                    _logger.LogError($"[Realtime API error: {msg}]");
                    FaultSession();
                    throw new InvalidOperationException($"Realtime session error: {msg}");
                }
            }

            // The receive stream ended cleanly without us cancelling and without an explicit
            // error update. Common causes: server-side max session duration reached, idle
            // timeout, or backend rotation. Treat as a session end so the existing reconnect
            // path engages on the next RunAsync. FaultSession decides whether the user actually
            // sees red eyes - only when a RunAsync is currently active (mid-conversation).
            if (!sessionToken.IsCancellationRequested)
            {
                _logger.LogWarning("[Receive stream ended without explicit error update - treating as session end.]");
                FaultSession();
                throw new InvalidOperationException("Realtime session ended unexpectedly.");
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation (Dispose / FaultSession after we already published the event).
        }
        // No general catch (Exception) - any other receive-side exception (e.g. WebSocketException
        // when the server abruptly closes the connection) faults the task. RunAsync's finally
        // observes and logs the fault; the audio loop's next operation will typically also fail
        // with State='Aborted' which is what reaches SystemService for the user-facing recovery.
    }

    /// <summary>
    /// Side-effect helper invoked when the realtime session is determined to be dead from the
    /// receive task's side.
    ///
    /// Behaviour depends on <see cref="_runActive"/>:
    /// <list type="bullet">
    /// <item><description>
    /// <b>Active conversation</b> (<c>_runActive == true</c>): publish
    /// <see cref="SystemErrorEvent"/> for immediate user feedback (red eyes), and cancel the
    /// session CTS so the linked audio capture loop unblocks and unwinds. RunAsync then observes
    /// the receive task fault and rethrows, which triggers SystemService to dispose + recreate
    /// the agent.
    /// </description></item>
    /// <item><description>
    /// <b>Idle</b> (<c>_runActive == false</c>, e.g. the session was being kept alive in the
    /// background while waiting for the next wake word): do NOT publish SystemErrorEvent - this
    /// is not a user-visible failure. If we did publish it, the eyes would stay red until the
    /// next wake word activation since SystemService only republishes <c>SystemOkEvent</c> at
    /// the top of each main-loop iteration. Still cancel the session CTS so any in-flight
    /// reads/writes give up promptly. The receive task will fault, the next RunAsync invocation
    /// observes <c>_receiveTask.IsCompleted</c> and silently reconnects via
    /// <c>ResetSessionAsync</c>.
    /// </description></item>
    /// </list>
    ///
    /// The actual error message is propagated through the receive task's Task.Exception, not
    /// through shared state.
    /// </summary>
    private void FaultSession()
    {
        if (_runActive)
        {
            try
            {
                _bus.Publish<SystemErrorEvent>(this);
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[FaultSession publish failed: {ex.Message}]");
            }
        }
        else
        {
            // Background session end while idle - suppress the user-visible event but keep a
            // breadcrumb so the log narrates what happened.
            _logger.LogDebug("[FaultSession: no active RunAsync - suppressing SystemErrorEvent (background session end). Will reconnect on next wake word.]");
        }

        try
        {
            _sessionCts?.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[FaultSession cancel failed: {ex.Message}]");
        }
    }

    /// <summary>
    /// Safely write to speaker (may be null during audio device transitions).
    /// </summary>
    private void WriteSpeakerSafe(byte[] data)
    {
        lock (_speakerLock)
        {
            _currentSpeaker?.Write(data);
        }
    }

    /// <summary>
    /// Safely clear speaker buffer.
    /// </summary>
    private void ClearSpeakerSafe()
    {
        lock (_speakerLock)
        {
            _currentSpeaker?.Clear();
        }
    }

    /// <summary>
    /// Safely wait for speaker playback to finish.
    /// </summary>
    private async Task FlushSpeakerSafeAsync(CancellationToken cancellationToken)
    {
        Speaker speaker;
        lock (_speakerLock)
        {
            speaker = _currentSpeaker;
        }

        if (speaker != null)
            await speaker.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Audio capture loop - runs until cancellation or inactivity timeout.
    /// Neither cancellation nor timeout closes the session.
    /// </summary>
    private async Task<RealtimeAgentRunResult> AudioCaptureLoopAsync(
        RealtimeSessionClient session,
        PvRecorder recorder,
        Speaker speaker,
        SileroVadDetector vadDetector,
        CancellationToken cancellationToken)
    {
        var audioBuffer = new List<short>();
        var preBuffer = new Queue<short[]>();
        bool isRecording = false;
        int speechFrameCount = 0;
        int silenceFrameCount = 0;
        bool wasModelSpeaking = false;
        var lastActivityUtc = DateTime.UtcNow;

        // Rolling history of the last VAD speech probabilities, purely for diagnostic logging
        // when barge-in fires. Lets us tell apart a marginal trigger (e.g. 0.04, 0.06, 0.51,
        // 0.52) from a strong one (e.g. 0.92, 0.95, 0.96) without enabling per-frame trace.
        const int VadProbHistorySize = 10;
        var recentSpeechProbs = new Queue<float>(VadProbHistorySize);

        recorder.Start();
        speaker.Start();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = recorder.Read();

                // Downsample from recorder rate to VAD rate if needed
                var vadFrame = AudioResampler.DownsampleForVad(frame, recorder.SampleRate, VadSampleRate, FrameLength);

                // Convert to float for VAD
                var floatFrame = new float[vadFrame.Length];
                for (int i = 0; i < vadFrame.Length; i++)
                {
                    floatFrame[i] = vadFrame[i] / 32768.0f;
                }

                // Run VAD
                float speechProb = vadDetector.Process(floatFrame);
                bool isSpeech = speechProb >= VadThreshold;
                if (isSpeech)
                    lastActivityUtc = DateTime.UtcNow;

                // Record into rolling history (for diagnostic logging at barge-in trigger).
                if (recentSpeechProbs.Count == VadProbHistorySize)
                    recentSpeechProbs.Dequeue();
                recentSpeechProbs.Enqueue(speechProb);

                // Reset inactivity timer when model finishes speaking
                if (wasModelSpeaking && !_modelIsSpeaking)
                    lastActivityUtc = DateTime.UtcNow;
                wasModelSpeaking = _modelIsSpeaking;

                // If model is speaking and we detect speech, trigger barge-in
                if (_modelIsSpeaking && isSpeech)
                {
                    speechFrameCount++;
                    if (speechFrameCount >= MinSpeechFramesForBargeIn)
                    {
                        // Calculate playback position before clearing
                        // 24kHz mono 16-bit = 48000 bytes/second
                        string truncateItemId;
                        int audioEndMs;
                        
                        lock (_outputAudioLock)
                        {
                            _bargeInTriggered = true;
                            truncateItemId = _currentStreamingItemId;
                            audioEndMs = (int)((_audioBytesSentToSpeaker / 48000.0) * 1000);
                        }
                        
                        var speechProbStr = speechProb.ToString("0.00");
                        var probHistory = string.Join(", ", recentSpeechProbs.Select(p => p.ToString("0.00")));
                        _logger.LogWarning($"[Barge-in detected - interrupting model at {audioEndMs}ms, ItemId={truncateItemId}, speechProb={speechProbStr}, recentProbs=[{probHistory}]]");

                        ClearSpeakerSafe(); // Clear buffered audio immediately

                        //try
                        //{
                        //    await session.CancelResponseAsync(cancellationToken);

                        //    await session.TruncateItemAsync(truncateItemId, 0, TimeSpan.FromMilliseconds(audioEndMs), cancellationToken);
                        //}
                        //catch (Exception ex)
                        //{
                        //    _logger.LogDebug($"[Cancel/truncate failed: {ex.Message}]");
                        //}

                        if (_responseActive)
                        {
                            try
                            {
                                await session.CancelResponseAsync(cancellationToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug($"[Cancel response failed: {ex.Message}]");
                            }

                            if (truncateItemId is not null)
                            {
                                try
                                {
                                    await session.TruncateItemAsync(truncateItemId, 0, TimeSpan.FromMilliseconds(audioEndMs), cancellationToken);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning($"[Truncate failed: {ex.Message}]");
                                }
                            }
                        }

                        _modelIsSpeaking = false;
                        _stateUpdateAction?.Invoke(StateUpdate.SpeakingStopped);
                        isRecording = true;
                        audioBuffer.Clear();
                        vadDetector.Reset();
                        speechFrameCount = 0;
                        silenceFrameCount = 0;
                    }
                }

                // Maintain pre-buffer when not recording
                if (!isRecording)
                {
                    var upsampledFrame = AudioResampler.UpsampleTo24kHz(frame, recorder.SampleRate);
                    preBuffer.Enqueue(upsampledFrame);
                    while (preBuffer.Count > PreBufferFrames)
                        preBuffer.Dequeue();
                }

                // State machine for recording
                if (!isRecording && !_modelIsSpeaking)
                {
                    if (isSpeech)
                    {
                        speechFrameCount++;
                        if (speechFrameCount >= MinSpeechFrames)
                        {
                            _logger.LogDebug("[Voice detected - recording...]");
                            isRecording = true;
                            audioBuffer.Clear();

                            while (preBuffer.Count > 0)
                                audioBuffer.AddRange(preBuffer.Dequeue());

                            silenceFrameCount = 0;
                        }
                    }
                    else
                        speechFrameCount = 0;
                }
                else if (isRecording)
                {
                    var upsampledFrame = AudioResampler.UpsampleTo24kHz(frame, recorder.SampleRate);
                    audioBuffer.AddRange(upsampledFrame);

                    if (isSpeech)
                        silenceFrameCount = 0;
                    else
                    {
                        silenceFrameCount++;
                        if (silenceFrameCount >= SilenceFramesToStop)
                        {
                            _logger.LogDebug("[Silence detected - sending to model...]");

                            var audioBytes = AudioResampler.ShortsToBytes(audioBuffer.ToArray());
                            await session.SendInputAudioAsync(new MemoryStream(audioBytes), cancellationToken);
                            await session.CommitPendingAudioAsync(cancellationToken);
                            await session.StartResponseAsync(cancellationToken);
                            _waitingForResponse = true;
                            _responseActive = true;
                            _responseRequestedAtUtc = DateTime.UtcNow;

                            isRecording = false;
                            audioBuffer.Clear();
                            silenceFrameCount = 0;
                            speechFrameCount = 0;
                            vadDetector.Reset();
                        }
                    }
                }

                // Safety: if we've been waiting for a model response for too long, give up
                if (_waitingForResponse && (DateTime.UtcNow - _responseRequestedAtUtc).TotalSeconds > 30)
                {
                    _logger.LogWarning("[Response wait timeout - model did not respond within 30s]");
                    _waitingForResponse = false;
                }

                // Check for inactivity timeout (since robot finished talking and user hasn't responded)
                // Don't timeout while waiting for the model to respond to our request
                if (!isRecording && !_modelIsSpeaking && !_waitingForResponse)
                {
                    var inactivityTimeout = TimeSpan.FromSeconds(_options.ConversationInactivityTimeoutSeconds ?? 10);

                    if (DateTime.UtcNow - lastActivityUtc >= inactivityTimeout)
                    {
                        _logger.LogDebug("[Inactivity timeout - pausing audio capture...]");
                        return RealtimeAgentRunResult.InactivityTimeout;
                    }
                }

                // Small delay to prevent CPU spinning
                await Task.Delay(1, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested - session stays alive
        }
        finally
        {
            recorder.Stop();
            speaker.Stop();
        }

        return RealtimeAgentRunResult.Cancelled;
    }

    private static string GetItemId(RealtimeItem item) => item switch
    {
        RealtimeFunctionCallItem fc => fc.Id,
        RealtimeFunctionCallOutputItem fco => fco.Id,
        RealtimeMessageItem msg => msg.Id,
        _ => null
    };

    private RealtimeClient GetRealtimeConversationClient()
    {
        var apiKey = _options.OpenAiApiKey;
        var endpoint = _options.OpenAiEndpoint;

        if (!string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(endpoint))
            return new RealtimeClient(new ApiKeyCredential(apiKey));

        if (!string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(endpoint))
        {
            var client = new AzureOpenAIClient(
                endpoint: new Uri(endpoint),
                credential: new ApiKeyCredential(apiKey));
            return client.GetRealtimeClient();
        }

        throw new InvalidOperationException(
            "OpenAI/Azure OpenAI configuration was not found. " +
            "Please set OpenAiApiKey and optionally OpenAiEndpoint.");
    }

    /// <summary>
    /// Configures a conversation session with standard options (voice, audio format, instructions, tools).
    /// </summary>
    private async Task ConfigureSessionAsync(RealtimeSessionClient session, CancellationToken cancellationToken)
    {
        var sessionOptions = new RealtimeConversationSessionOptions()
        {
            Instructions = _options.Instructions,
            AudioOptions = new RealtimeConversationSessionAudioOptions
            {
                InputAudioOptions = new RealtimeConversationSessionInputAudioOptions
                {
                    AudioFormat = new RealtimePcmAudioFormat(),
                },
                OutputAudioOptions = new RealtimeConversationSessionOutputAudioOptions
                {
                    AudioFormat = new RealtimePcmAudioFormat(),
                    Voice = _options.Voice is not null ? new RealtimeVoice(_options.Voice) : null,
                    Speed = _options.SpeechSpeed
                }
            }
        };

        // Disable server-side VAD so only our local Silero VAD controls turn detection.
        // Setting TurnDetection = null does NOT work - the serializer omits the field entirely,
        // causing the server to use its default (VAD enabled). DisableTurnDetection() uses the
        // SDK's JsonPatch to explicitly send "turn_detection": null in the JSON payload.
        sessionOptions.AudioOptions.InputAudioOptions.DisableTurnDetection();

        if (_tools != null && _tools.Count > 0)
        {
            foreach (var tool in ToolInvoker.ConvertToRealtimeTools(_tools))
            {
                _logger.LogDebug($"[Adding tool: {tool.FunctionName}: {tool.FunctionDescription}]");
                sessionOptions.Tools.Add(tool);
            }

            if (sessionOptions.Tools.Count > 0)
                sessionOptions.ToolChoice = RealtimeDefaultToolChoice.Auto;
        }

        await session.ConfigureConversationSessionAsync(sessionOptions, cancellationToken);
    }
}


