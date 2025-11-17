using Microsoft.Extensions.Logging;
using NanoBot.Configuration;
using NanoBot.Events;
using NanoBot.Util;
using NanoWakeWord;
using Pv;

namespace NanoBot.Services;

public interface IWakeWordService
{
    public Task<string> WaitForWakeWordAsync(CancellationToken cancellationToken = default);
}

internal class WakeWordProcessorState
{
    public Queue<short[]> PreBuffer { get; } = new();
    public Queue<short[]> SpeechBuffer { get; } = new();
    public int NonSilentFrameCount { get; set; }
    public bool VadActive { get; set; }
    public bool SpeechDetected { get; set; }
    public int SilenceFrameCount { get; set; }
    public int SpeechFrameCount { get; set; }
}

public class WakeWordService : IWakeWordService
{
    // Calibration parameters for silence detection
    public const int SilenceSampleAmplitudeThreshold = 1200;//800;
    
    private readonly ILogger<WakeWordService> _logger;
    private readonly IDynamicOptions<AppConfig> _appConfigOptions;
    private readonly IEventBus _bus;

    // VAD parameters
    private const int SampleRate = 16000;
    private const float VadThreshold = 0.4f; //0.5f;
    private const int MinSilenceFrames = 50; // ~1.6 seconds of silence at 16kHz
    //private const int VadAbortSilenceFrames = 20; // ~0.64 seconds of silence
    private const int VadAbortSilenceFrames = 40; // ~1.28 seconds of silence before abort
    
    // Buffer parameters
    private const int PreBufferLength = 10; // keep a small history to include wake onset
    private const int MaxSpeechBufferFrames = 100; // ~3 seconds at 16kHz
    
    // Detection thresholds
    private const int NoiseActivationFrameCount = 5;
    private const int SpeechConfirmationFrameCount = 3;

    public WakeWordService(
        ILogger<WakeWordService> logger,
        IDynamicOptions<AppConfig> appConfigOptions,
        IEventBus bus)
    {
        _logger = logger;
        _appConfigOptions = appConfigOptions;
        _bus = bus;

        typeof(WakeWordService).Assembly.ExtractModels(); // Extract local wake word models (from embedded resources)
    }
    
    /// <summary>
    /// Noise Detection → VAD Activation → Speech Confirmation → Wake Word Processing
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public async Task<string> WaitForWakeWordAsync(CancellationToken cancellationToken = default)
    {
        var wakeWordRuntimeConfig = new WakeWordRuntimeConfig
        {
            DebugAction = (model, probability, detected) => { _logger.LogDebug($"{model} {probability:F5} - {detected}", model, probability, detected); },
            WakeWords = GetWakeWordConfig()
        };
        var wakeWordRuntime = new WakeWordRuntime(wakeWordRuntimeConfig);

        // Pre-warm the wake word engine with silent frames to avoid first-activation delay
        _logger.LogDebug("Pre-warming wake word engine...");
        var silentFrame = new short[512]; // 512 samples of silence
        for (var i = 0; i < 50; i++) // Process 50 silent frames to warm up
            wakeWordRuntime.Process(silentFrame);

        _logger.LogDebug($"{nameof(WaitForWakeWordAsync)} ENTER.");

        // Initialize and start PvRecorder
        using var recorder = PvRecorder.Create(frameLength: 512, deviceIndex: -1);
        _logger.LogDebug($"Using device: {recorder.SelectedDevice}");
        var listenWakeWords = string.Join(',', wakeWordRuntimeConfig.WakeWords.Select(t => t.Model));
        _logger.LogDebug($"Listening for [{@listenWakeWords}]...");
        recorder.Start();

        // Register cancellation callback to stop recorder when cancellation is requested
        var stopped = false;
        await using var registration = cancellationToken.Register(() => 
        {
            if (!stopped)
            {
                stopped = true;
                try { recorder.Stop(); } catch { } // Best effort
            }
        });

        // Initialize VAD detector
        using var vadDetector = new SileroVadDetector(SampleRate);
        vadDetector.Reset();

        // Run the speech processing loop as a separate task
        var res = await Task.Run(async () =>
        {            
            try
            {
                var state = new WakeWordProcessorState();

                var vadActive = false;

                while (recorder.IsRecording && !cancellationToken.IsCancellationRequested)
                {
                    var frame = recorder.Read();
                    var result = ProcessFrame(
                        frame,
                        vadDetector,
                        wakeWordRuntime,
                        wakeWordRuntimeConfig,
                        state);

                    if (state.VadActive && !vadActive)
                    {
                        _bus.Publish<NoiseDetectedEvent>(this);
                        vadActive = true;
                    }
                    else if (!state.VadActive && vadActive)
                    {
                        _bus.Publish<SilenceDetectedEvent>(this);
                        vadActive = false;
                    }

                    if (result != null)
                        return result;
                }

                // Check if we exited due to cancellation before throwing
                if (cancellationToken.IsCancellationRequested)
                    return null;

                throw new Exception($"{nameof(WaitForWakeWordAsync)} failed.");
            }
            finally
            {
                _logger.LogDebug($"{nameof(WaitForWakeWordAsync)} EXIT.");
            }
        }, cancellationToken);

        return res;
    }
    
    private string ProcessFrame(
        short[] frame,
        SileroVadDetector vadDetector,
        WakeWordRuntime wakeWordRuntime,
        WakeWordRuntimeConfig wakeWordRuntimeConfig,
        WakeWordProcessorState state)
    {        
        // Noise Detection → VAD Activation → Speech Confirmation → Wake Word Processing

        if (!state.VadActive && !state.SpeechDetected) // Stage 1: Noise detection - look for non-silence
        {
            
            var isSilent = frame.All(t => Math.Abs((int)t) < SilenceSampleAmplitudeThreshold);
            if (!isSilent)
            {
                state.NonSilentFrameCount++;

                // Activate VAD after a few consecutive non-silent frames
                if (state.NonSilentFrameCount >= NoiseActivationFrameCount)
                {
                    state.VadActive = true;
                    state.SpeechFrameCount = 0;
                    state.SilenceFrameCount = 0;
                    _logger.LogDebug("Noise detected, activating VAD...");

                    // Move pre-buffer frames to speech buffer (will be processed after VAD confirms speech)
                    while (state.PreBuffer.Count > 0)
                        state.SpeechBuffer.Enqueue(state.PreBuffer.Dequeue());
                }
            }
            else
            {                
                state.NonSilentFrameCount = 0; // reset if we dip back to silence
            }

            // Maintain pre-buffer while idle
            state.PreBuffer.Enqueue(frame);
            if (state.PreBuffer.Count > PreBufferLength)
                state.PreBuffer.Dequeue();
        }
        else if (state.VadActive && !state.SpeechDetected) // Stage 2: VAD detection - confirm it's actual speech
        {            
            var floatFrame = NormalizeFrame(frame);
            var speechProb = vadDetector.Process(floatFrame);
            var isSpeech = speechProb >= VadThreshold;

            // Buffer frame while waiting for VAD confirmation (don't process yet)
            state.SpeechBuffer.Enqueue(frame);

            if (isSpeech)
            {
                state.SpeechFrameCount++;

                // Confirm speech after a few consecutive speech frames
                if (state.SpeechFrameCount >= SpeechConfirmationFrameCount)
                {
                    state.SpeechDetected = true;
                    state.SilenceFrameCount = 0;
                    _logger.LogDebug($"Speech confirmed by VAD (prob: {speechProb.ToString("F3")}), processing buffered frames...");

                    // Process buffered frames with wake word engine now that VAD confirmed speech
                    var framesToProcess = new List<short[]>(state.SpeechBuffer);
                    foreach (var buffered in framesToProcess)
                    {
                        var bufferedIndex = wakeWordRuntime.Process(buffered);
                        if (bufferedIndex >= 0)
                        {
                            var wakeWord = wakeWordRuntimeConfig.WakeWords[bufferedIndex].Model;
                            _logger.LogDebug($"[{DateTime.Now.ToLongTimeString()}] Detected '{wakeWord}' from buffered frames.");
                            return wakeWord;
                        }
                    }
                }
            }
            else
            {
                state.SpeechFrameCount = 0; // Reset counter if silence is detected

                // If sustained silence, abort VAD and return to noise detection
                state.SilenceFrameCount++;
                if (state.SilenceFrameCount > VadAbortSilenceFrames)
                {
                    state.VadActive = false;
                    state.NonSilentFrameCount = 0;
                    state.SpeechBuffer.Clear();
                    state.PreBuffer.Clear();
                    vadDetector.Reset();
                    _logger.LogDebug("VAD aborted due to sustained silence, returning to noise detection.");
                }
            }
        }
        else if (state.SpeechDetected) // Stage 3: Real-time wake word processing during speech
        {            
            var floatFrame = NormalizeFrame(frame);
            var speechProb = vadDetector.Process(floatFrame);
            var isSpeech = speechProb >= VadThreshold;

            // Add frame to limited buffer (wake words are short, so we don't need a huge buffer)
            state.SpeechBuffer.Enqueue(frame);
            if (state.SpeechBuffer.Count > MaxSpeechBufferFrames)
                state.SpeechBuffer.Dequeue(); // Keep buffer size limited

            // Process frame with wake word engine in real-time
            var index = wakeWordRuntime.Process(frame);
            if (index >= 0)
            {
                var wakeWord = wakeWordRuntimeConfig.WakeWords[index].Model;
                _logger.LogDebug($"[{DateTime.Now.ToLongTimeString()}] Detected '{wakeWord}' during speech.");

                return wakeWord;
            }

            if (isSpeech)
                state.SilenceFrameCount = 0;
            else
                state.SilenceFrameCount++;

            // When speech ends, reset and return to noise detection
            if (state.SilenceFrameCount > MinSilenceFrames)
            {
                _logger.LogDebug($"Speech ended (VAD prob: {speechProb.ToString("F3")}), no wake word detected.");
                state.VadActive = false;
                state.SpeechDetected = false;
                state.NonSilentFrameCount = 0;
                state.SpeechBuffer.Clear();
                state.PreBuffer.Clear();
                vadDetector.Reset();
            }
        }

        return null;
    }

    private WakeWordConfig[] GetWakeWordConfig()
    {
        var appConfig = _appConfigOptions.Value;

        WakeWordConfig[] wakeWords;
        if (appConfig.Agents != null)
            wakeWords = appConfig.Agents.Where(t => !t.Disabled).Select(t => new WakeWordConfig
            {
                Model = t.WakeWord,
                Threshold = t.WakeWordThreshold > 0 ? t.WakeWordThreshold : 0.5f,
                TriggerLevel = t.WakeWordTriggerLevel > 0 ? t.WakeWordTriggerLevel : 4,
            }).ToArray();
        else
            wakeWords = [];

        return wakeWords;
    }
    
    private static float[] NormalizeFrame(short[] frame)
    {
        // Convert short[] to float[] (normalize to -1.0 to 1.0 range)
        var floatFrame = new float[frame.Length];
        for (var i = 0; i < frame.Length; i++)
            floatFrame[i] = frame[i] / 32768.0f;

        return floatFrame;
    }
}

