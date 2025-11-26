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
    public bool SpeechDetected { get; set; }
    public int SilenceFrameCount { get; set; }
    public bool NoiseDetected { get; set; }
}

public class WakeWordService : IWakeWordService
{
    private readonly ILogger<WakeWordService> _logger;
    private readonly IDynamicOptions<AppConfig> _appConfigOptions;
    private readonly IEventBus _bus;

    // Buffer parameters
    private const int PreBufferLength = 10; // keep a small history to include wake onset
    private const int MaxSpeechBufferFrames = 100; // ~3 seconds at 16kHz
    
    // Detection thresholds
    private const int NoiseActivationFrameCount = 5;
    private const int MinSilenceFrames = 50; // ~1.6 seconds of silence at 16kHz

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

        // Get configuration values
        var appConfig = _appConfigOptions.Value;
        var silenceThreshold = appConfig.WakeWordSilenceSampleAmplitudeThreshold;
        _logger.LogDebug($"Silence threshold: {silenceThreshold}");

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

        // Run the speech processing loop as a separate task
        var res = await Task.Run(async () =>
        {            
            try
            {
                var state = new WakeWordProcessorState();

                var noiseDetected = false;

                while (recorder.IsRecording && !cancellationToken.IsCancellationRequested)
                {
                    var frame = recorder.Read();
                    var result = ProcessFrame(
                        frame,
                        wakeWordRuntime,
                        wakeWordRuntimeConfig,
                        state,
                        silenceThreshold);

                    // Publish noise/silence events based on noise detection state
                    // Skip events if silence threshold is <= 0 (noise detection effectively disabled)
                    if (silenceThreshold > 0)
                    {
                        if (state.NoiseDetected && !noiseDetected)
                        {
                            _bus.Publish<NoiseDetectedEvent>(this);
                            noiseDetected = true;
                        }
                        else if (!state.NoiseDetected && noiseDetected)
                        {
                            _bus.Publish<SilenceDetectedEvent>(this);
                            noiseDetected = false;
                        }
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
        WakeWordRuntime wakeWordRuntime,
        WakeWordRuntimeConfig wakeWordRuntimeConfig,
        WakeWordProcessorState state,
        int silenceThreshold)
    {        
        if (!state.SpeechDetected) // Stage 1: Noise detection - look for non-silence (or skip if threshold <= 0)
        {
            // If threshold <= 0, skip noise detection and go directly to wake word processing
            if (silenceThreshold <= 0)
            {
                state.SilenceFrameCount = 0;

                // Move pre-buffer frames to speech buffer
                while (state.PreBuffer.Count > 0)
                    state.SpeechBuffer.Enqueue(state.PreBuffer.Dequeue());

                // Add current frame to speech buffer
                state.SpeechBuffer.Enqueue(frame);

                // Skip directly to wake word processing
                state.SpeechDetected = true;
                _logger.LogDebug("Noise detection disabled (threshold <= 0), processing wake words directly...");

                // Process buffered frames immediately
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
            else
            {
                // Normal noise detection when threshold > 0
                var isSilent = frame.All(t => Math.Abs((int)t) < silenceThreshold);
                if (!isSilent)
                {
                    state.NonSilentFrameCount++;

                    // Activate processing after a few consecutive non-silent frames
                    if (state.NonSilentFrameCount >= NoiseActivationFrameCount)
                    {
                        state.SilenceFrameCount = 0;
                        state.NoiseDetected = true; // Mark noise as detected

                        // Move pre-buffer frames to speech buffer
                        while (state.PreBuffer.Count > 0)
                            state.SpeechBuffer.Enqueue(state.PreBuffer.Dequeue());

                        // Skip directly to wake word processing
                        state.SpeechDetected = true;
                        _logger.LogDebug("Noise detected, processing wake words...");

                        // Process buffered frames immediately
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
                    state.NonSilentFrameCount = 0; // reset if we dip back to silence
                    // Ensure noise is not detected when we're back in idle state
                    if (!state.SpeechDetected)
                    {
                        state.NoiseDetected = false;
                    }
                }

                // Maintain pre-buffer while idle
                state.PreBuffer.Enqueue(frame);
                if (state.PreBuffer.Count > PreBufferLength)
                    state.PreBuffer.Dequeue();
            }
        }
        else // Stage 2: Real-time wake word processing during speech
        {            
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

            // Use noise detection to determine when to reset
            var isSilent = frame.All(t => Math.Abs((int)t) < silenceThreshold);
            if (isSilent)
                state.SilenceFrameCount++;
            else
                state.SilenceFrameCount = 0;

            // When silence detected, reset and return to noise detection
            if (state.SilenceFrameCount > MinSilenceFrames)
            {
                _logger.LogDebug("Silence detected, no wake word detected.");
                state.SpeechDetected = false;
                state.NonSilentFrameCount = 0;
                state.NoiseDetected = false; // Mark noise as no longer detected
                state.SpeechBuffer.Clear();
                state.PreBuffer.Clear();
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
}

