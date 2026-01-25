using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Realtime;
using Pv;
using System.ClientModel;
using System.Text;
using System.Text.Json;

namespace NanoBot.Util;

public class RealtimeConversationAgentOptions
{
    public string Model { get; set; } = "gpt-4o-mini-realtime-preview";
    public string Voice { get; set; } = "marin";
    public string Instructions { get; set; }
    public string OpenAiApiKey { get; set; }
    public string OpenAiEndpoint { get; set; }
    public float? Temperature { get; set; }
}

public enum RealtimeConversationAgentRealtimeRunResult
{
    Cancelled,
    InactivityTimeout,
}

/// <summary>
/// Manages a realtime conversation session with OpenAI using local VAD (Voice Activity Detection).
/// The session persists across multiple RunAsync calls until the object is disposed.
/// </summary>
public sealed class RealtimeConversationAgent : IAsyncDisposable, IDisposable
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
    private static readonly TimeSpan InactivityTimeout = TimeSpan.FromSeconds(30);

    private readonly Kernel _kernel;
    private readonly ILogger _logger;
    private readonly RealtimeConversationAgentOptions _options;
    private readonly object _speakerLock = new();
    private readonly Dictionary<string, StringBuilder> _functionArgumentBuildersById = new();

    private RealtimeClient _realtimeClient;
    private RealtimeSession _session;
    private bool _disposed;

    // Receive task runs for the lifetime of the session (not per RunAsync call)
    private Task _receiveTask;
    // Session CTS is independent - only cancelled on dispose, not on RunAsync cancellation
    private CancellationTokenSource? _sessionCts;

    // Shared state between receive task and audio loop
    private PvSpeaker? _currentSpeaker;
    private volatile bool _modelIsSpeaking;
    private volatile bool _bargeInTriggered;

    public RealtimeConversationAgent(ILogger<RealtimeConversationAgent> logger, Kernel kernel, IOptions<RealtimeConversationAgentOptions> options)
    {
        _logger = logger;
        _kernel = kernel;
        _options = options.Value;
    }

    /// <summary>
    /// Runs the conversation loop until cancellation is requested or VAD inactivity timeout occurs.
    /// Both cancellation and timeout preserve the session - call DisposeAsync to close the session.
    /// </summary>
    /// <returns>Why the loop returned.</returns>
    public async Task<RealtimeConversationAgentRealtimeRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Get the Realtime client
        _realtimeClient ??= GetRealtimeConversationClient();

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

        using var recorder = PvRecorder.Create(frameLength: FrameLength, deviceIndex: -1);
        using var speaker = new PvSpeaker(sampleRate: SampleRate, bitsPerSample: 16, bufferSizeSecs: 60);
        using var vadDetector = new SileroVadDetector(VadSampleRate);

        // Set current speaker for receive task to use
        lock (_speakerLock)
        {
            _currentSpeaker = speaker;
        }

        _logger.LogDebug($"Using microphone: {recorder.SelectedDevice}");

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
        {
            _receiveTask = RunReceiveTaskAsync(_session, _sessionCts!.Token);
        }

        // Start the conversation loop (audio capture)
        var result = await AudioCaptureLoopAsync(_session, recorder, speaker, vadDetector, cancellationToken);

        // Clear speaker reference when exiting
        lock (_speakerLock)
        {
            _currentSpeaker = null;
        }

        return result;
    }

    /// <summary>
    /// Resets the session state asynchronously, cleaning up the old session.
    /// </summary>
    private async Task ResetSessionAsync()
    {
        if (_sessionCts is not null)
        {
            await _sessionCts.CancelAsync();
        }

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

        if (_session is IAsyncDisposable asyncDisposable)
        {
            try
            {
                await asyncDisposable.DisposeAsync();
            }
            catch
            {
                // Ignore errors during cleanup
            }
        }
        else if (_session is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _sessionCts?.Dispose();
        _session = null;
        _sessionCts = null;
        _receiveTask = null;
    }

    /// <summary>
    /// Disposes the conversation, closing the session gracefully.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_session is not null)
        {
            _logger.LogDebug("[Closing session...]");

            // Cancel the session to stop the receive task
            if (_sessionCts is not null)
            {
                await _sessionCts.CancelAsync();
            }

            // Wait for receive task to complete
            if (_receiveTask is not null)
            {
                try
                {
                    await _receiveTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
                catch
                {
                    // Ignore errors during shutdown
                }
            }

            // Dispose the session
            if (_session is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (_session is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _logger.LogDebug("[Session closed]");
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
        if (_disposed) return;
        _disposed = true;

        // Cancel the session
        _sessionCts?.Cancel();

        // Don't wait for receive task in sync dispose - just cancel and move on

        // Dispose the session
        if (_session is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _sessionCts?.Dispose();

        _session = null;
        _sessionCts = null;
        _receiveTask = null;
    }

    /// <summary>
    /// Receive task that runs for the lifetime of the session.
    /// Uses the session-level cancellation token, not the per-RunAsync token.
    /// </summary>
    private async Task RunReceiveTaskAsync(RealtimeSession session, CancellationToken sessionToken)
    {
        var outputAudioBuffer = new List<byte>();
        const int SpeakerChunkSize = 16384;

        try
        {
            await foreach (RealtimeUpdate update in session.ReceiveUpdatesAsync(sessionToken))
            {
                if (sessionToken.IsCancellationRequested) break;

                // Session started
                if (update is ConversationSessionStartedUpdate sessionStartedUpdate)
                {
                    _logger.LogDebug($"[Session started: {sessionStartedUpdate.SessionId}]");
                }

                // Model started generating response
                if (update is OutputStreamingStartedUpdate streamingStartedUpdate)
                {
                    _modelIsSpeaking = true;
                    _bargeInTriggered = false;
                    outputAudioBuffer.Clear();

                    _logger.LogDebug($"[OutputStreamingStarted: FunctionName={streamingStartedUpdate.FunctionName ?? "null"}, ItemId={streamingStartedUpdate.ItemId ?? "null"}]");

                    if (!string.IsNullOrEmpty(streamingStartedUpdate.FunctionName))
                    {
                        Console.Write($"[Calling: {streamingStartedUpdate.FunctionName}] ");
                    }
                }

                // Streaming delta (audio, text, function args)
                if (update is OutputDeltaUpdate deltaUpdate)
                {
                    if (!string.IsNullOrEmpty(deltaUpdate.AudioTranscript))
                    {
                        Console.Write(deltaUpdate.AudioTranscript);
                    }
                    if (!string.IsNullOrEmpty(deltaUpdate.Text))
                    {
                        _logger.LogDebug($"[TextDelta: {deltaUpdate.Text}]");
                        Console.Write(deltaUpdate.Text);
                    }

                    // Handle audio bytes
                    if (deltaUpdate.AudioBytes is not null && !_bargeInTriggered)
                    {
                        var audioBytes = deltaUpdate.AudioBytes.ToArray();
                        outputAudioBuffer.AddRange(audioBytes);

                        while (outputAudioBuffer.Count >= SpeakerChunkSize)
                        {
                            byte[] chunk = new byte[SpeakerChunkSize];
                            outputAudioBuffer.CopyTo(0, chunk, 0, SpeakerChunkSize);
                            outputAudioBuffer.RemoveRange(0, SpeakerChunkSize);
                            WriteSpeakerSafe(chunk);
                        }
                    }

                    // Collect function arguments
                    if (!string.IsNullOrWhiteSpace(deltaUpdate.FunctionArguments))
                    {
                        if (!_functionArgumentBuildersById.TryGetValue(deltaUpdate.ItemId, out var builder))
                        {
                            _functionArgumentBuildersById[deltaUpdate.ItemId] = builder = new StringBuilder();
                        }
                        builder.Append(deltaUpdate.FunctionArguments);

                        // Log additional delta update properties for function calls
                        _logger.LogDebug($"[FunctionArgsDelta: ItemId={deltaUpdate.ItemId}, FunctionCallId={deltaUpdate.FunctionCallId ?? "null"}, Args={deltaUpdate.FunctionArguments}]");
                    }
                }

                // Item finished streaming
                if (update is OutputStreamingFinishedUpdate streamingFinishedUpdate)
                {
                    _logger.LogDebug($"[OutputStreamingFinished: FunctionCallId={streamingFinishedUpdate.FunctionCallId ?? "null"}, FunctionName={streamingFinishedUpdate.FunctionName ?? "null"}, ItemId={streamingFinishedUpdate.ItemId ?? "null"}]");
                    
                    if (streamingFinishedUpdate.FunctionCallId is not null)
                    {
                        if (_kernel == null)
                        {
                            throw new InvalidOperationException(
                                $"Function '{streamingFinishedUpdate.FunctionName}' was called but kernel is null. " +
                                "A kernel must be provided when tools are configured in the session.");
                        }

                        _logger.LogDebug($"[Executing function: {streamingFinishedUpdate.FunctionName}]");

                        RealtimeItem functionOutputItem;
                        try
                        {
                            functionOutputItem = await InvokeFunctionAsync(
                                streamingFinishedUpdate.FunctionName,
                                streamingFinishedUpdate.FunctionCallId,
                                streamingFinishedUpdate.ItemId,
                                _functionArgumentBuildersById,
                                _kernel,
                                sessionToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"[Function '{streamingFinishedUpdate.FunctionName}' failed: {ex.Message}]");
                            
                            // Return error to model so it can respond appropriately
                            functionOutputItem = RealtimeItem.CreateFunctionCallOutput(
                                callId: streamingFinishedUpdate.FunctionCallId,
                                output: $"Error: {ex.Message}");
                        }

                        await session.AddItemAsync(functionOutputItem, sessionToken);
                    }
                    else if (streamingFinishedUpdate.MessageContentParts?.Count > 0)
                    {
                        
                    }
                }

                // Input audio transcription completed (only if whisper transcription is enabled)
                if (update is InputAudioTranscriptionFinishedUpdate transcriptionUpdate)
                {
                    _logger.LogDebug($"[You said: {transcriptionUpdate.Transcript}]");
                }

                // Response finished
                if (update is ResponseFinishedUpdate responseFinishedUpdate)
                {
                    // Write any remaining buffered audio
                    if (outputAudioBuffer.Count > 0 && !_bargeInTriggered)
                    {
                        byte[] remainingChunk = new byte[outputAudioBuffer.Count];
                        outputAudioBuffer.CopyTo(remainingChunk, 0);
                        WriteSpeakerSafe(remainingChunk);
                        outputAudioBuffer.Clear();
                    }

                    FlushSpeakerSafe();
                    _modelIsSpeaking = false;

                    // Log created items for debugging
                    _logger.LogDebug($"[ResponseFinished: {responseFinishedUpdate.CreatedItems.Count} items created]");
                    foreach (var item in responseFinishedUpdate.CreatedItems)
                    {
                        _logger.LogDebug($"  - Item: FunctionName={item.FunctionName ?? "null"}, FunctionCallId={item.FunctionCallId ?? "null"}, MessageRole={item.MessageRole}");
                    }

                    if (responseFinishedUpdate.CreatedItems.Any(item => item.FunctionName?.Length > 0))
                    {
                        _logger.LogDebug("[Function calls detected - triggering response...]");
                        await session.StartResponseAsync(sessionToken);
                    }
                    else
                    {
                        _logger.LogDebug("[Ready for your next question...]");
                    }
                }

                // Handle errors
                if (update is RealtimeErrorUpdate errorUpdate)
                {
                    _logger.LogError($"[Error: {errorUpdate.Message}]");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when session is cancelled (dispose)
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Receive task error: {ex.Message}]");
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
    /// Safely flush speaker.
    /// </summary>
    private void FlushSpeakerSafe()
    {
        lock (_speakerLock)
        {
            _currentSpeaker?.Flush();
        }
    }

    /// <summary>
    /// Audio capture loop - runs until cancellation or inactivity timeout.
    /// Neither cancellation nor timeout closes the session.
    /// </summary>
    private async Task<RealtimeConversationAgentRealtimeRunResult> AudioCaptureLoopAsync(
        RealtimeSession session,
        PvRecorder recorder,
        PvSpeaker speaker,
        SileroVadDetector vadDetector,
        CancellationToken cancellationToken)
    {
        var audioBuffer = new List<short>();
        var preBuffer = new Queue<short[]>();
        bool isRecording = false;
        int speechFrameCount = 0;
        int silenceFrameCount = 0;
        var lastSpeechUtc = DateTime.UtcNow;

        recorder.Start();
        speaker.Start();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = recorder.Read();

                // Downsample from recorder rate to VAD rate if needed
                var vadFrame = DownsampleForVad(frame, recorder.SampleRate, VadSampleRate, FrameLength);

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
                {
                    lastSpeechUtc = DateTime.UtcNow;
                }

                // If model is speaking and we detect speech, trigger barge-in
                if (_modelIsSpeaking && isSpeech)
                {
                    speechFrameCount++;
                    if (speechFrameCount >= MinSpeechFramesForBargeIn)
                    {
                        _logger.LogDebug("[Barge-in detected - interrupting model]");

                        _bargeInTriggered = true;
                        speaker.Stop();

                        try
                        {
                            await session.CancelResponseAsync(cancellationToken);
                        }
                        catch
                        {
                            // Response may have already finished
                        }

                        speaker.Start();

                        _modelIsSpeaking = false;
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
                    var upsampledFrame = UpsampleTo24kHz(frame, recorder.SampleRate);
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
                    {
                        speechFrameCount = 0;
                    }
                }
                else if (isRecording)
                {
                    var upsampledFrame = UpsampleTo24kHz(frame, recorder.SampleRate);
                    audioBuffer.AddRange(upsampledFrame);

                    if (isSpeech)
                    {
                        silenceFrameCount = 0;
                    }
                    else
                    {
                        silenceFrameCount++;
                        if (silenceFrameCount >= SilenceFramesToStop)
                        {
                            _logger.LogDebug("[Silence detected - sending to model...]");

                            var audioBytes = ShortsToBytes(audioBuffer.ToArray());
                            await session.SendInputAudioAsync(new MemoryStream(audioBytes), cancellationToken);
                            await session.CommitPendingAudioAsync(cancellationToken);
                            await session.StartResponseAsync(cancellationToken);

                            isRecording = false;
                            audioBuffer.Clear();
                            silenceFrameCount = 0;
                            speechFrameCount = 0;
                            vadDetector.Reset();
                        }
                    }
                }

                // Check for inactivity timeout
                if (!isRecording && !_modelIsSpeaking)
                {
                    if (DateTime.UtcNow - lastSpeechUtc >= InactivityTimeout)
                    {
                        _logger.LogDebug("[Inactivity timeout - pausing audio capture...]");
                        return RealtimeConversationAgentRealtimeRunResult.InactivityTimeout;
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

        return RealtimeConversationAgentRealtimeRunResult.Cancelled;
    }

    private short[] DownsampleForVad(short[] input, int inputRate, int outputRate, int outputLength)
    {
        if (inputRate == outputRate && input.Length == outputLength)
            return input;

        var result = new short[outputLength];
        double ratio = (double)inputRate / outputRate;

        for (int i = 0; i < outputLength; i++)
        {
            int srcIndex = (int)(i * ratio);
            if (srcIndex < input.Length)
                result[i] = input[srcIndex];
        }

        return result;
    }

    private short[] UpsampleTo24kHz(short[] input, int inputRate)
    {
        if (inputRate == 24000)
            return input;

        double ratio = 24000.0 / inputRate;
        int outputLength = (int)(input.Length * ratio);
        var result = new short[outputLength];

        for (int i = 0; i < outputLength; i++)
        {
            int srcIndex = (int)(i / ratio);
            if (srcIndex < input.Length)
                result[i] = input[srcIndex];
        }

        return result;
    }

    private byte[] ShortsToBytes(short[] shorts)
    {
        var bytes = new byte[shorts.Length * 2];
        Buffer.BlockCopy(shorts, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private RealtimeClient GetRealtimeConversationClient()
    {
        if (!string.IsNullOrEmpty(_options.OpenAiApiKey) && string.IsNullOrEmpty(_options.OpenAiEndpoint))
        {
            return new RealtimeClient(new ApiKeyCredential(_options.OpenAiApiKey));
        }
        else if (!string.IsNullOrEmpty(_options.OpenAiApiKey) && string.IsNullOrEmpty(_options.OpenAiEndpoint))
        {
            var client = new AzureOpenAIClient(
                endpoint: new Uri(_options.OpenAiEndpoint),
                credential: new ApiKeyCredential(_options.OpenAiApiKey));

            return client.GetRealtimeClient();
        }
        else
        {
            throw new Exception("OpenAI/Azure OpenAI configuration was not found. " +
                "Please set your API key in user secrets or environment variables.");
        }
    }

    #region ToolHelpers
    private const string FunctionNameSeparator = "-";

    /// <summary>
    /// Invokes a function call from the Realtime API and returns the output item.
    /// </summary>
    public async Task<RealtimeItem> InvokeFunctionAsync(
        string functionName,
        string functionCallId,
        string itemId,
        Dictionary<string, StringBuilder> functionArgumentBuildersById,
        Kernel kernel,
        CancellationToken cancellationToken)
    {
        var (parsedFunctionName, pluginName) = ParseFunctionName(functionName);
        var argumentsString = functionArgumentBuildersById.GetValueOrDefault(itemId)?.ToString() ?? "{}";
        var arguments = DeserializeArguments(argumentsString);

        var functionCallContent = new FunctionCallContent(
            functionName: parsedFunctionName,
            pluginName: pluginName,
            id: functionCallId,
            arguments: arguments);

        var resultContent = await functionCallContent.InvokeAsync(kernel, cancellationToken);

        return RealtimeItem.CreateFunctionCallOutput(
            callId: functionCallId,
            output: ProcessFunctionResult(resultContent.Result));
    }

    /// <summary>
    /// Configures a conversation session with standard options (voice, audio format, instructions, tools).
    /// </summary>
    private async Task ConfigureSessionAsync(RealtimeSession session, CancellationToken cancellationToken)
    {
        var sessionOptions = new ConversationSessionOptions()
        {
            Voice = _options.Voice,
            InputAudioFormat = RealtimeAudioFormat.Pcm16,
            OutputAudioFormat = RealtimeAudioFormat.Pcm16,
            Instructions = _options.Instructions,
            TurnDetectionOptions = TurnDetectionOptions.CreateDisabledTurnDetectionOptions(), // Disable server-side VAD - we handle it locally
            Temperature = _options.Temperature
        };

        if (_kernel != null)
        {
            foreach (var tool in ConvertFunctions(_kernel))
            {
                _logger.LogDebug($"[Adding tool: {tool.Name}: {tool.Description}]");
                sessionOptions.Tools.Add(tool);
            }

            if (sessionOptions.Tools.Count > 0)
                sessionOptions.ToolChoice = ConversationToolChoice.CreateAutoToolChoice();
        }

        await session.ConfigureConversationSessionAsync(sessionOptions, cancellationToken);
    }

    /// <summary>
    /// Converts Semantic Kernel plugins to OpenAI Realtime conversation tools.
    /// </summary>
    public IEnumerable<ConversationFunctionTool> ConvertFunctions(Kernel kernel)
    {
        foreach (var plugin in kernel.Plugins)
        {
            var functionsMetadata = plugin.GetFunctionsMetadata();

            foreach (var metadata in functionsMetadata)
            {
                var toolDefinition = metadata.ToOpenAIFunction().ToFunctionDefinition(false);

                yield return new ConversationFunctionTool(name: toolDefinition.FunctionName)
                {
                    Description = toolDefinition.FunctionDescription,
                    Parameters = toolDefinition.FunctionParameters
                };
            }
        }
    }

    /// <summary>
    /// Parses a fully qualified function name into its component parts.
    /// Format: "PluginName-FunctionName" or just "FunctionName".
    /// </summary>
    public (string FunctionName, string PluginName) ParseFunctionName(string fullyQualifiedName)
    {
        string pluginName = null;
        string functionName = fullyQualifiedName;

        int separatorPos = fullyQualifiedName.IndexOf(FunctionNameSeparator, StringComparison.Ordinal);
        if (separatorPos >= 0)
        {
            pluginName = fullyQualifiedName.AsSpan(0, separatorPos).Trim().ToString();
            functionName = fullyQualifiedName.AsSpan(separatorPos + FunctionNameSeparator.Length).Trim().ToString();
        }

        return (functionName, pluginName);
    }

    /// <summary>
    /// Deserializes JSON arguments string to KernelArguments.
    /// </summary>
    public KernelArguments DeserializeArguments(string argumentsString)
    {
        var arguments = JsonSerializer.Deserialize<KernelArguments>(argumentsString);

        if (arguments is not null)
        {
            var names = arguments.Names.ToArray();
            foreach (var name in names)
            {
                arguments[name] = arguments[name]?.ToString();
            }
        }

        return arguments;
    }

    /// <summary>
    /// Processes a function result into a string suitable for the Realtime API.
    /// </summary>
    public string ProcessFunctionResult(object? functionResult)
    {
        if (functionResult is string stringResult)
        {
            return stringResult;
        }

        return JsonSerializer.Serialize(functionResult);
    }
    #endregion 
}


