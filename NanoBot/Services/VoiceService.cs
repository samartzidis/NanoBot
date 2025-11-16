using Microsoft.CognitiveServices.Speech;
using Microsoft.Extensions.Logging;
using NanoBot.Configuration;
using NanoBot.Util;
using OpenAI;
using OpenAI.Audio;
using Pv;
using System.Text;
using System.Text.RegularExpressions;

namespace NanoBot.Services;

public enum ReceiveVoiceMessageResult { Ok, RecordingTimeExceeded, RecordingTimeout, RecordingCancelled }

public class VoiceInfo
{
    public string ShortName { get; set; }
    public string Locale { get; set; }
    public SynthesisVoiceGender Gender { get; set; }
}

public interface IVoiceService
{
    ReceiveVoiceMessageResult WaitForSpeech(out byte[] audioBuffer, CancellationToken cancellationToken = default);
    string GenerateSpeechToText(byte[] audioBuffer, string audioTranscriptionLanguage);
    Task GenerateTextToSpeechAsync(string text, string speechSynthesisVoiceName, CancellationToken cancellationToken = default);
    Task<List<VoiceInfo>> GetAvailableVoicesAsync(TextToSpeechServiceProviderConfig config, CancellationToken cancellationToken = default);
}

public class VoiceService : IVoiceService
{
    // Calibration parameters for silence detection
    public const int SilenceSampleCountThreshold = 50;

    //User voice message max recording duration if no silence is detected
    public const int MaxRecordingDurationSeconds = 30;

    //Silence duration to mark end of recording of user voice message
    public const int StopRecordingSilenceSeconds = 5;

    private readonly string _openAiTextToSpeechModel = "tts-1";
    private readonly string _openAiSpeechToTextModel = "whisper-1";
    private readonly string[] _openAiVoiceNames = ["alloy", "echo", "fable", "onyx", "nova", "shimmer", "ash", "sage", "coral"];

    private List<VoiceInfo> _cachedVoices; // Cache variable for storing OpenAI voices
    private readonly ILogger _logger;
    private readonly IDynamicOptions<AppConfig> _appConfigOptions;
    private readonly int _pvRecordingDeviceIndex = -1; // use default

    public VoiceService(
        ILogger<VoiceService> logger,
        IDynamicOptions<AppConfig> appConfigOptions)
    {
        _logger = logger;
        _appConfigOptions = appConfigOptions;
    }

    public string GenerateSpeechToText(byte[] audioBuffer, string audioTranscriptionLanguage)
    {
        var appConfig = _appConfigOptions.Value;

        var openAiClient = new OpenAIClient(appConfig.OpenAiApiKey);
        var whisperClient = openAiClient.GetAudioClient(_openAiSpeechToTextModel);

        using var memoryStream = new MemoryStream(audioBuffer);
        AudioTranscription at = whisperClient.TranscribeAudio(memoryStream, "audio.wav", options: new AudioTranscriptionOptions
        {
            Language = audioTranscriptionLanguage
        });

        return at.Text;
    }

    /*
    // Commented out as OpenAI's "whisper-1" currently gives more accurate results
    public string TranscribeSpeech(byte[] audioBuffer, string audioTranscriptionLanguage)
    {
        _logger.LogDebug($"{nameof(TranscribeSpeech)}: {nameof(audioBuffer)}={audioBuffer.Length}, {nameof(audioTranscriptionLanguage)}=audioTranscriptionLanguage ");

        var speechConfig = SpeechConfig.FromSubscription(_appConfig.SpeechServiceKey, _appConfig.SpeechServiceRegion);
        speechConfig.SpeechRecognitionLanguage = audioTranscriptionLanguage;
        speechConfig.SetProperty(PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs, "5000"); // 5 seconds for initial silence
        speechConfig.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "2000"); // 2 seconds for end silence

        using var audioStream = AudioInputStream.CreatePushStream();
        using var audioConfig = AudioConfig.FromStreamInput(audioStream);
        using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

        audioStream.Write(audioBuffer);
        audioStream.Close(); // This is important or the RecognizeOnceAsync might hang

        var result = recognizer.RecognizeOnceAsync().ConfigureAwait(false).GetAwaiter().GetResult();

        if (result.Reason == ResultReason.RecognizedSpeech)
        {
            _logger.LogDebug($"RecognizedSpeech: {result.Text}");
            return result.Text;
        }
        else if (result.Reason == ResultReason.NoMatch)
        {
            _logger.LogDebug("NoMatch: Speech could not be recognized.");
        }
        else if (result.Reason == ResultReason.Canceled)
        {
            var cancellation = CancellationDetails.FromResult(result);
            _logger.LogDebug($"Canceled: Reason={cancellation.Reason}");

            if (cancellation.Reason == CancellationReason.Error)
            {
                _logger.LogDebug($"Canceled: ErrorCode={cancellation.ErrorCode}");
                _logger.LogDebug($"Canceled: ErrorDetails={cancellation.ErrorDetails}");
            }
        }

        return null;
    }

    private int GetPvRecordingDeviceIndexByName(string deviceName)
    {
        var audioDevices = PvRecorder.GetAvailableDevices();
        _logger.LogDebug($"Available devices: {string.Join(", ", audioDevices)}");

        var deviceIndex = Array.FindIndex(audioDevices, t => t.StartsWith(deviceName, StringComparison.OrdinalIgnoreCase)); // Will be set to -1 if not found
        _logger.LogDebug($"Matching device index for \"{deviceName}\": {deviceIndex}");

        return deviceIndex;
    }
    */

    public async Task GenerateTextToSpeechAsync(string text, string speechSynthesisVoiceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Input text cannot be null or empty.", nameof(text));

        if (string.IsNullOrWhiteSpace(speechSynthesisVoiceName))
            throw new ArgumentException("Voice name cannot be null or empty.", nameof(speechSynthesisVoiceName));

        var appConfig = _appConfigOptions.Value;

        if (appConfig.VoiceService.TextToSpeechServiceProvider == TextToSpeechServiceProviderConfig.AzureSpeechService)
            await GenerateTextToSpeechAzureAsync(text, speechSynthesisVoiceName, cancellationToken);
        else if (appConfig.VoiceService.TextToSpeechServiceProvider == TextToSpeechServiceProviderConfig.OpenAI)
            await GenerateTextToSpeechOpenAiAsync(text, speechSynthesisVoiceName, cancellationToken);
        else
            throw new InvalidOperationException($"{nameof(TextToSpeechServiceProviderConfig)} value {appConfig.VoiceService.TextToSpeechServiceProvider} is unsupported.");
    }

    public async Task GenerateTextToSpeechAzureAsync(string text, string speechSynthesisVoiceName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug($"{nameof(GenerateTextToSpeechAsync)} ENTER.");

        var appConfig = _appConfigOptions.Value;

        var speechConfig = SpeechConfig.FromSubscription(appConfig.VoiceService.AzureSpeechServiceKey, appConfig.VoiceService.AzureSpeechServiceRegion);
        speechConfig.SpeechSynthesisVoiceName = speechSynthesisVoiceName;

        var synthesizer = new SpeechSynthesizer(speechConfig);

        try
        {
            // "await using" will dispose the cancellationToken registration at end of scope
            // before the finalizer executes and disposes off the synthesizer.
            await using var registration = cancellationToken.Register(() => {
                synthesizer.StopSpeakingAsync().GetAwaiter().GetResult();
            });

            // Split text into batches of 400 characters max, due to known bug: https://github.com/Azure-Samples/cognitive-services-speech-sdk/issues/2350
            var sentences = SplitTextIntoSentences(text, 400);

            foreach (var sentence in sentences)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug($"{nameof(GenerateTextToSpeechAsync)} Cancelled.");
                    break;
                }

                _logger.LogDebug($"Sentence batch: {sentence}");
                var result = await synthesizer.SpeakTextAsync(sentence).ConfigureAwait(false);
                if (result.Reason == ResultReason.Canceled)
                {
                    _logger.LogDebug($"{nameof(GenerateTextToSpeechAsync)} Cancelled.");
                    break;
                }
            }
        }
        finally
        {
            _logger.LogDebug($"{nameof(GenerateTextToSpeechAsync)} EXIT.");

            synthesizer.Dispose();
        }
    }

    public async Task GenerateTextToSpeechOpenAiAsync(string text, string speechSynthesisVoiceName, CancellationToken cancellationToken = default)
    {
        var appConfig = _appConfigOptions.Value;

        var openAiClient = new OpenAIClient(appConfig.OpenAiApiKey);
        var audioClient = openAiClient.GetAudioClient(_openAiTextToSpeechModel);

        var speechVoice = new GeneratedSpeechVoice(speechSynthesisVoiceName);

        BinaryData res = await audioClient.GenerateSpeechAsync(
            text,
            speechVoice,
            new SpeechGenerationOptions { ResponseFormat = GeneratedSpeechFormat.Wav }, cancellationToken);

        var dataStream = res.ToStream();
        var tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
        try
        {
            await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await dataStream.CopyToAsync(fileStream, cancellationToken);
            }

            _logger.LogDebug($"Audio saved to temporary file: {tempFilePath}");

            await WavPlayerUtil.PlayAsync(tempFilePath, cancellationToken);
        }
        finally
        {
            File.Delete(tempFilePath);
        }
    }
   
    public ReceiveVoiceMessageResult WaitForSpeech(
        out byte[] audioBuffer,
        CancellationToken cancellationToken = default)
    {
        audioBuffer = null;

        const int sampleRate = 16000;
        const float vadThreshold = 0.5f;
        const int frameLength = 512; // Must match window size for 16kHz sample rate
        const int minSilenceFrames = 50; // Equivalent to ~1.6 seconds of silence at 16kHz
        const int preBufferLength = 10; // Keep a small history to include speech onset

        using var recorder = PvRecorder.Create(frameLength: frameLength, deviceIndex: _pvRecordingDeviceIndex);
        _logger.LogDebug($"Using device: {recorder.SelectedDevice}");

        recorder.Start();

        // Register cancellation callback to stop recorder when cancellation is requested
        var stopped = false;
        using var registration = cancellationToken.Register(() => {
            if (!stopped)
            {
                stopped = true;
                try
                {
                    recorder.Stop();
                }
                catch 
                { 
                    // Ignore errors when stopping during cancellation
                }
            }
        });

        using var vadDetector = new SileroVadDetector(sampleRate);
        vadDetector.Reset();

        var recordingBuffer = new List<short>();
        var preBuffer = new Queue<short[]>();
        var hasStartedRecording = false;
        var silenceFrameCount = 0;
        var speechFrameCount = 0;
        var startTime = DateTime.UtcNow;

        while (recorder.IsRecording)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                recorder.Stop();
                audioBuffer = GetAudioBufferBytes(recorder, recordingBuffer);
                return ReceiveVoiceMessageResult.RecordingCancelled;
            }

            var frame = recorder.Read();

            // Convert short[] to float[] (normalize to -1.0 to 1.0 range)
            var floatFrame = new float[frame.Length];
            for (var i = 0; i < frame.Length; i++)
            {
                floatFrame[i] = frame[i] / 32768.0f;
            }

            // Process frame and get speech probability
            var speechProb = vadDetector.Process(floatFrame);
            var isSpeech = speechProb >= vadThreshold;

            // Return on silence threshold exceeded (before recording starts)
            if (!hasStartedRecording && (DateTime.UtcNow - startTime).TotalSeconds > StopRecordingSilenceSeconds)
            {
                _logger.LogDebug($"Timeout: No voice detected within {StopRecordingSilenceSeconds} seconds.");
                recorder.Stop();
                audioBuffer = GetAudioBufferBytes(recorder, recordingBuffer);
                return ReceiveVoiceMessageResult.RecordingTimeout;
            }

            if (!hasStartedRecording)
            {
                if (isSpeech)
                {
                    speechFrameCount++;

                    // Activate recording after a few consecutive speech frames
                    if (speechFrameCount >= 3)
                    {
                        _logger.LogDebug($"Voice detected (VAD prob: {speechProb.ToString("F3")}), starting recording...");
                        hasStartedRecording = true;
                        startTime = DateTime.UtcNow;
                        silenceFrameCount = 0;

                        // Flush pre-buffer first so we capture the start of speech
                        while (preBuffer.Count > 0)
                            recordingBuffer.AddRange(preBuffer.Dequeue());
                    }
                }
                else
                {
                    speechFrameCount = 0; // Reset counter if silence is detected
                }

                // Maintain pre-buffer while idle
                preBuffer.Enqueue(frame);
                if (preBuffer.Count > preBufferLength)
                    preBuffer.Dequeue();
            }
            else // hasStartedRecording
            {
                if (isSpeech)
                {
                    silenceFrameCount = 0;
                }
                else
                {
                    silenceFrameCount++;
                }

                recordingBuffer.AddRange(frame);

                // Check maximum recording duration
                if ((DateTime.UtcNow - startTime).TotalSeconds > MaxRecordingDurationSeconds)
                {
                    _logger.LogDebug($"Recording exceeded maximum duration of {MaxRecordingDurationSeconds} seconds.");
                    recorder.Stop();
                    audioBuffer = GetAudioBufferBytes(recorder, recordingBuffer);
                    return ReceiveVoiceMessageResult.RecordingTimeExceeded;
                }

                // Stop recording after sustained silence
                if (silenceFrameCount > minSilenceFrames)
                {
                    _logger.LogDebug($"Stopped recording due to silence (VAD prob: {speechProb.ToString("F3")}).");
                    recorder.Stop();
                    break;
                }
            }
        }

        // Check if cancellation was requested (the callback may have stopped the recorder,
        // causing the loop to exit before the cancellation check inside the loop could run)
        if (cancellationToken.IsCancellationRequested)
        {
            audioBuffer = GetAudioBufferBytes(recorder, recordingBuffer);
            return ReceiveVoiceMessageResult.RecordingCancelled;
        }

        audioBuffer = GetAudioBufferBytes(recorder, recordingBuffer);
        return ReceiveVoiceMessageResult.Ok;
    }

    public async Task<List<VoiceInfo>> GetAvailableVoicesAsync(TextToSpeechServiceProviderConfig config, CancellationToken cancellationToken = default)
    {
        if (config == TextToSpeechServiceProviderConfig.OpenAI)
        {
            return _openAiVoiceNames.Select(v => new VoiceInfo { ShortName = v.ToString() }).ToList();
        }
        else if (config == TextToSpeechServiceProviderConfig.AzureSpeechService) // For AzureSpeechService we use caching
        {
            if (_cachedVoices != null)
            {
                _logger.LogDebug("Returning cached AzureSpeechService voices.");
                return _cachedVoices;
            }

            try
            {
                var appConfig = _appConfigOptions.Value;
                var speechConfig = SpeechConfig.FromSubscription(appConfig.VoiceService.AzureSpeechServiceKey, appConfig.VoiceService.AzureSpeechServiceRegion);
                using var synthesizer = new SpeechSynthesizer(speechConfig);
                var voicesResult = await synthesizer.GetVoicesAsync();

                if (voicesResult.Reason == ResultReason.VoicesListRetrieved)
                {
                    var res = voicesResult.Voices
                        .Select(v => new VoiceInfo { ShortName = v.ShortName, Locale = v.Locale, Gender = v.Gender })
                        .ToList();
                    _logger.LogDebug($"Retrieved {res.Count} voices.");

                    _cachedVoices = res;
                }
                else
                {
                    _logger.LogError($"Failed to retrieve AzureSpeechService voices: {voicesResult.Reason}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving AzureSpeechService available voices.");
            }
        }
        else
            throw new ArgumentException($"{nameof(TextToSpeechServiceProviderConfig)} value {config} is unsupported.");

        return _cachedVoices;
    }

    private byte[] GetAudioBufferBytes(PvRecorder recorder, List<short> recordingBuffer)
    {
        using var memoryStream = new MemoryStream();
        using var writer = new BinaryWriter(memoryStream);
        WriteWavHeader(writer, 1, 16, recorder.SampleRate, recordingBuffer.Count);
        foreach (var sample in recordingBuffer)
            writer.Write(sample);
        var audioBuffer = memoryStream.ToArray();

        return audioBuffer;
    }

    private void WriteWavHeader(BinaryWriter writer, ushort channelCount, ushort bitDepth, int sampleRate, int totalSampleCount)
    {
        writer.Seek(0, SeekOrigin.Begin);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write((bitDepth / 8 * totalSampleCount) + 36);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write(channelCount);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channelCount * bitDepth / 8);
        writer.Write((ushort)(channelCount * bitDepth / 8));
        writer.Write(bitDepth);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(bitDepth / 8 * totalSampleCount);
    }

    private List<string> SplitTextIntoSentences(string text, int maxCharsPerBatch)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        // Regular expression to split text by sentence boundaries
        var sentenceRegex = new Regex(@"(.*?[.!?]+(?:\s|$))", RegexOptions.Singleline);
        var matches = sentenceRegex.Matches(text);

        int lastIndex = 0;

        foreach (Match match in matches)
        {
            string sentence = match.Value.Trim();
            lastIndex += match.Length;

            // If the sentence is within the character limit, add it directly
            if (sentence.Length <= maxCharsPerBatch)
            {
                result.Add(sentence);
            }
            else
            {
                // Break the sentence into smaller parts if it exceeds the character limit
                result.AddRange(SplitLongSentence(sentence, maxCharsPerBatch));
            }
        }

        // Add any remaining text as a "sentence"
        if (lastIndex < text.Length)
        {
            string remainingText = text.Substring(lastIndex).Trim();
            if (!string.IsNullOrEmpty(remainingText))
            {
                if (remainingText.Length <= maxCharsPerBatch)
                {
                    result.Add(remainingText);
                }
                else
                {
                    result.AddRange(SplitLongSentence(remainingText, maxCharsPerBatch));
                }
            }
        }

        return result;
    }

    private List<string> SplitLongSentence(string sentence, int maxCharsPerBatch)
    {
        var parts = new List<string>();
        var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var currentPart = new List<string>();
        int currentLength = 0;

        foreach (var word in words)
        {
            // Check if adding this word would exceed the character limit
            if (currentLength + word.Length + (currentPart.Count > 0 ? 1 : 0) > maxCharsPerBatch)
            {
                // Add the current part to the list
                parts.Add(string.Join(" ", currentPart));
                currentPart.Clear();
                currentLength = 0;
            }

            // Add the word to the current part
            currentPart.Add(word);
            currentLength += word.Length + (currentPart.Count > 1 ? 1 : 0); // Account for space
        }

        // Add the last part if there are remaining words
        if (currentPart.Count > 0)
        {
            parts.Add(string.Join(" ", currentPart));
        }

        return parts;
    }
}