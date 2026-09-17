using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NanoBot.Util;

namespace NanoBot.Services;

public interface ILocalTtsService
{
    Task<string> TextToIpaAsync(string text, string voice = null, CancellationToken cancellationToken = default);

    Task SpeakAsync(string text, int? maxLength = null, CancellationToken cancellationToken = default);

    Task SpeakIpaAsync(string ipa, CancellationToken cancellationToken = default);
}

/// <summary>
/// Local, offline text-to-speech: phonemizes text to IPA via the local espeak-ng executable,
/// then synthesizes audio with a Piper (VITS) ONNX voice model and plays it back over the
/// shared <see cref="AudioOutputEngine"/>.
/// </summary>
public sealed class LocalTtsService : ILocalTtsService
{
    private static readonly string ExecutableName = PlatformUtil.IsWindowsPlatform() ? "espeak-ng.exe" : "espeak-ng";

    private readonly ILogger<LocalTtsService> _logger;
    private readonly AudioOutputEngine _audioOutputEngine;
    private readonly InferenceSession _session;
    private readonly PiperVoiceConfig _voiceConfig;

    public LocalTtsService(ILogger<LocalTtsService> logger, AudioOutputEngine audioOutputEngine)
    {
        _logger = logger;
        _audioOutputEngine = audioOutputEngine;

        var baseDir = AppContext.BaseDirectory;
        var modelPath = Path.Combine(baseDir, "Resources", "en_GB-alba-medium.int8.onnx");
        var configPath = modelPath + ".json";

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Required TTS model not found: {modelPath}");

        _voiceConfig = PiperVoiceConfig.Load(configPath);
        _session = new InferenceSession(modelPath);
    }

    public async Task<string> TextToIpaAsync(string text, string voice = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = ExecutableName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (!string.IsNullOrWhiteSpace(voice))
            {
                processStartInfo.ArgumentList.Add("-v");
                processStartInfo.ArgumentList.Add(voice);
            }

            processStartInfo.ArgumentList.Add("--ipa");
            processStartInfo.ArgumentList.Add("-q");
            processStartInfo.ArgumentList.Add(text);

            using var process = new Process { StartInfo = processStartInfo };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                _logger.LogError($"{ExecutableName} exited with code {process.ExitCode}: {stderr}");
                return string.Empty;
            }

            return stdout.ToString().Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, $"Failed to run {ExecutableName} for text-to-IPA phonemization. Is it installed and on PATH?");
            return string.Empty;
        }
    }

    public async Task SpeakAsync(string text, int? maxLength = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (maxLength.HasValue && text.Length > maxLength.Value)
            text = TruncateAtWordBoundary(text, maxLength.Value);

        var ipa = await TextToIpaAsync(text, _voiceConfig.Espeak.Voice, cancellationToken);
        await SpeakIpaAsync(ipa, cancellationToken);
    }

    public async Task SpeakIpaAsync(string ipa, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ipa))
            return;

        var ids = PhonemesToIds(ipa, _voiceConfig.PhonemeIdMap);
        if (ids.Count <= 3)
        {
            _logger.LogWarning($"No known phonemes produced for IPA input: {ipa}");
            return;
        }

        var audio = Synthesize(ids);
        var pcm = FloatToPcm16(audio);
        var resampled = AudioResampler.UpsampleTo24kHz(pcm, _voiceConfig.Audio.SampleRate);
        var bytes = AudioResampler.ShortsToBytes(resampled);

        using var speaker = new Speaker(_audioOutputEngine, bitsPerSample: 16);
        speaker.Start();
        try
        {
            speaker.Write(bytes);
            await speaker.FlushAsync(cancellationToken);
        }
        finally
        {
            speaker.Stop();
        }
    }

    // Truncates to at most maxLength characters, backing up to the closest preceding whitespace
    // so we don't cut a word in half. If there's no word boundary at all within the limit (e.g.
    // a single very long token), there's nothing that fits without cutting a word, so returns
    // empty rather than cutting mid-word.
    private static string TruncateAtWordBoundary(string text, int maxLength)
    {
        var cut = text.LastIndexOf(' ', maxLength - 1);
        return cut > 0 ? text[..cut] : string.Empty;
    }

    // Piper phoneme-id scheme: ^ <pad> (p0 <pad> p1 <pad> ... pn <pad>) $
    private static List<long> PhonemesToIds(string phonemes, Dictionary<string, int[]> phonemeIdMap)
    {
        var ids = new List<long>
        {
            phonemeIdMap["^"][0],
            phonemeIdMap["_"][0]
        };

        foreach (var c in phonemes)
        {
            if (!phonemeIdMap.TryGetValue(c.ToString(), out var value))
                continue;

            ids.Add(value[0]);
            ids.Add(phonemeIdMap["_"][0]);
        }

        ids.Add(phonemeIdMap["$"][0]);
        return ids;
    }

    private float[] Synthesize(List<long> phonemeIds)
    {
        var inputTensor = new DenseTensor<long>(phonemeIds.ToArray(), new[] { 1, phonemeIds.Count });
        var inputLengthsTensor = new DenseTensor<long>(new long[] { phonemeIds.Count }, new[] { 1 });
        var scalesTensor = new DenseTensor<float>(
            new[] { _voiceConfig.Inference.NoiseScale, _voiceConfig.Inference.LengthScale, _voiceConfig.Inference.NoiseW },
            new[] { 3 });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor),
            NamedOnnxValue.CreateFromTensor("input_lengths", inputLengthsTensor),
            NamedOnnxValue.CreateFromTensor("scales", scalesTensor)
        };

        if (_session.InputMetadata.ContainsKey("sid"))
        {
            var sidTensor = new DenseTensor<long>(new long[] { 0 }, new[] { 1 });
            inputs.Add(NamedOnnxValue.CreateFromTensor("sid", sidTensor));
        }

        using var results = _session.Run(inputs);
        return results.First().AsEnumerable<float>().ToArray();
    }

    private static short[] FloatToPcm16(float[] audio)
    {
        var pcm = new short[audio.Length];
        for (var i = 0; i < audio.Length; i++)
        {
            var v = audio[i] * 32767f;
            v = Math.Clamp(v, short.MinValue, short.MaxValue);
            pcm[i] = (short)v;
        }
        return pcm;
    }
}

internal sealed class PiperVoiceConfig
{
    [JsonPropertyName("audio")]
    public PiperAudioConfig Audio { get; set; } = new();

    [JsonPropertyName("espeak")]
    public PiperEspeakConfig Espeak { get; set; } = new();

    [JsonPropertyName("inference")]
    public PiperInferenceConfig Inference { get; set; } = new();

    [JsonPropertyName("phoneme_id_map")]
    public Dictionary<string, int[]> PhonemeIdMap { get; set; } = new();

    public static PiperVoiceConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PiperVoiceConfig>(json)
            ?? throw new InvalidDataException($"Failed to parse Piper voice config: {path}");
    }
}

internal sealed class PiperAudioConfig
{
    [JsonPropertyName("sample_rate")]
    public int SampleRate { get; set; } = 22050;
}

internal sealed class PiperEspeakConfig
{
    [JsonPropertyName("voice")]
    public string Voice { get; set; }
}

internal sealed class PiperInferenceConfig
{
    [JsonPropertyName("noise_scale")]
    public float NoiseScale { get; set; } = 0.667f;

    [JsonPropertyName("length_scale")]
    public float LengthScale { get; set; } = 1f;

    [JsonPropertyName("noise_w")]
    public float NoiseW { get; set; } = 0.8f;
}
