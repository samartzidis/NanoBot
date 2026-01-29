using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;
using SoundFlow.Visualization;

namespace NanoBot.Util;

public class Speaker : IDisposable
{
    private readonly int _sampleRate;
    private readonly int _bitsPerSample;
    private readonly int _bufferSizeSecs;
    private readonly int _deviceIndex;
    private bool _isStarted;
    private bool _disposed;
    private readonly object _bufferLock = new();
    private AudioEngine _engine;
    private AudioPlaybackDevice _playbackDevice;
    private QueueDataProvider _dataProvider;
    private SoundPlayer _soundPlayer;
    private readonly AudioFormat _audioFormat;
    private LevelMeterAnalyzer _levelMeter;
    private Timer _vuMeterTimer;
    private Action<byte> _vuMeterAction;
    private const int VuMeterUpdateIntervalMs = 50; // Update 20 times per second

    // PCM normalization divisors: convert signed integer samples to -1.0 to 1.0 float range
    private const float Pcm16BitMaxValue = 32768.0f;      // 2^15 (16-bit signed max + 1)
    private const float Pcm24BitMaxValue = 8388608.0f;    // 2^23 (24-bit signed max + 1)
    private const float Pcm32BitMaxValue = 2147483648.0f; // 2^31 (32-bit signed max + 1)

    /// <summary>
    /// Initializes a new instance of PvSpeaker.
    /// </summary>
    /// <param name="sampleRate">Sample rate in Hz</param>
    /// <param name="bitsPerSample">Bits per sample (typically 16 or 24)</param>
    /// <param name="bufferSizeSecs">Size of internal PCM buffer in seconds</param>
    /// <param name="deviceIndex">Index of output audio device (-1 for default)</param>
    /// <param name="preferredBackends">Optional array of preferred audio backends to use (e.g., PvSpeaker.LinuxAlsaOnly on Linux to avoid probing warnings)</param>
    public Speaker(int sampleRate, int bitsPerSample, int bufferSizeSecs = 20, int deviceIndex = -1, MiniAudioBackend[] preferredBackends = null)
    {
        _sampleRate = sampleRate;
        _bitsPerSample = bitsPerSample;
        _bufferSizeSecs = bufferSizeSecs;
        _deviceIndex = deviceIndex;
        _isStarted = false;
        _disposed = false;

        // Create audio format based on sample rate and bits per sample
        // Use F32 format since we convert PCM bytes to float samples
        _audioFormat = new AudioFormat
        {
            Format = SampleFormat.F32,
            Channels = 1, // Mono as per PvSpeaker requirements
            Layout = ChannelLayout.Mono,
            SampleRate = sampleRate
        };

        // Initialize SoundFlow engine with preferred backends (if specified)
        _engine = new MiniAudioEngine(preferredBackends);
    }

    /// <summary>
    /// Registers an optional callback to receive VU meter updates during playback.
    /// The callback will be called periodically with values ranging from 0-255.
    /// </summary>
    /// <param name="action">Callback function that receives VU meter values (0-255)</param>
    public void SetVuMeterAction(Action<byte> action)
    {
        _vuMeterAction = action;

        // If already started and callback was just registered, add level meter
        if (_isStarted && action != null && _soundPlayer != null && _levelMeter == null)
        {
            _levelMeter = new LevelMeterAnalyzer(_audioFormat);
            _soundPlayer.AddAnalyzer(_levelMeter);
            _vuMeterTimer = new Timer(UpdateVuMeter, null, VuMeterUpdateIntervalMs, VuMeterUpdateIntervalMs);
        }
        // If callback was removed and already started, remove level meter
        else if (_isStarted && action == null && _levelMeter != null && _playbackDevice != null)
        {
            _playbackDevice.MasterMixer.RemoveAnalyzer(_levelMeter);
            _levelMeter = null;
            _vuMeterTimer?.Dispose();
            _vuMeterTimer = null;
        }
    }

    /// <summary>
    /// Updates the VU meter by reading the level meter and calling the registered callback.
    /// </summary>
    private void UpdateVuMeter(object state)
    {
        if (_disposed || _levelMeter == null || _vuMeterAction == null)
            return;

        // Only update VU meter when there's audio data in the playback buffer
        if (_dataProvider == null || _dataProvider.SamplesAvailable == 0)
            return;

        try
        {
            // Get Peak level (more responsive than RMS for VU meter display)
            float peak = _levelMeter.Peak;

            // Use logarithmic (dB) scale for perceptually-accurate VU meter
            // Convert linear amplitude to dB: dB = 20 * log10(amplitude)
            // Map -60 dB to 0 dB range to 0-255 range
            byte level;
            if (peak <= 0.000001f) // Essentially silence
            {
                level = 0;
            }
            else
            {
                // Convert to dB (0 dB = full scale, negative values = quieter)
                float dB = 20.0f * MathF.Log10(peak);

                // Map dB range to 0-255
                // -60 dB -> 0, 0 dB -> 255
                const float minDb = -60.0f;
                const float maxDb = 0.0f;
                float normalized = (dB - minDb) / (maxDb - minDb);
                normalized = Math.Max(0.0f, Math.Min(1.0f, normalized));
                level = (byte)(normalized * 255.0f);
            }

            // Call the callback
            _vuMeterAction(level);
        }
        catch
        {
            // Ignore errors in VU meter updates to prevent disrupting playback
        }
    }

    /// <summary>
    /// Gets a list of available audio output devices.
    /// </summary>
    /// <param name="preferredBackends">Optional array of preferred audio backends to use (e.g., PvSpeaker.LinuxAlsaOnly on Linux)</param>
    /// <returns>Array of device names</returns>
    public static string[] GetAvailableDevices(MiniAudioBackend[]? preferredBackends = null)
    {
        try
        {
            using var engine = new MiniAudioEngine(preferredBackends);
            engine.UpdateAudioDevicesInfo();
            var devices = engine.PlaybackDevices;

            if (devices.Length == 0)
            {
                return new[] { "Default Output Device" };
            }

            return devices.Select(d => d.Name).ToArray();
        }
        catch
        {
            // Return default device if enumeration fails
            return new[] { "Default Output Device" };
        }
    }

    /// <summary>
    /// Starts the audio playback engine.
    /// </summary>
    public void Start()
    {
        if (_isStarted || _disposed || _engine == null)
            return;

        try
        {
            _engine.UpdateAudioDevicesInfo();
            var devices = _engine.PlaybackDevices;

            DeviceInfo deviceInfo;
            if (_deviceIndex >= 0 && _deviceIndex < devices.Length)
            {
                deviceInfo = devices[_deviceIndex];
            }
            else if (devices.Length > 0)
            {
                // Find default device or use first available
                var defaultDeviceArray = devices.Where(d => d.IsDefault).ToArray();
                deviceInfo = defaultDeviceArray.Length > 0 ? defaultDeviceArray[0] : devices[0];
            }
            else
            {
                throw new InvalidOperationException("No playback devices available");
            }

            // Create device configuration with low-latency settings for responsive VU metering
            // Small period size = lower latency but higher CPU usage
            const uint lowLatencyPeriodFrames = 512; // ~10ms at 48kHz
            var deviceConfig = new MiniAudioDeviceConfig
            {
                PeriodSizeInFrames = lowLatencyPeriodFrames,
                Periods = 2, // Double buffering
                Playback = new DeviceSubConfig
                {
                    ShareMode = ShareMode.Shared
                },
                Wasapi = new WasapiSettings
                {
                    Usage = WasapiUsage.ProAudio
                }
            };

            // Initialize playback device
            _playbackDevice = _engine.InitializePlaybackDevice(deviceInfo, _audioFormat, deviceConfig);

            // Create queue-based data provider for streaming PCM data
            // QueueDataProvider is designed for dynamic streaming scenarios
            int bufferSizeSamples = _sampleRate * _bufferSizeSecs;
            _dataProvider = new QueueDataProvider(_audioFormat, maxSamples: bufferSizeSamples, QueueFullBehavior.Block);

            // Create sound player
            _soundPlayer = new SoundPlayer(_engine, _audioFormat, _dataProvider);

            // Add player to mixer
            _playbackDevice.MasterMixer.AddComponent(_soundPlayer);

            // Add level meter analyzer to MasterMixer (not SoundPlayer) if callback is registered
            // Adding to MasterMixer ensures we analyze the final output in sync with actual playback
            if (_vuMeterAction != null)
            {
                _levelMeter = new LevelMeterAnalyzer(_audioFormat);
                _playbackDevice.MasterMixer.AddAnalyzer(_levelMeter);
            }

            // Start the device
            _playbackDevice.Start();

            // Start VU meter timer if callback is registered
            if (_vuMeterAction != null && _levelMeter != null)
            {
                _vuMeterTimer = new Timer(UpdateVuMeter, null, VuMeterUpdateIntervalMs, VuMeterUpdateIntervalMs);
            }

            _isStarted = true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to start audio playback: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Writes PCM audio data to the playback buffer.
    /// </summary>
    /// <param name="pcmData">PCM audio data bytes</param>
    /// <returns>Number of samples written (not bytes)</returns>
    public int Write(byte[] pcmData)
    {
        if (_disposed || !_isStarted || _dataProvider == null)
            return 0;

        if (pcmData == null || pcmData.Length == 0)
            return 0;

        int bytesPerSample = _bitsPerSample / 8;
        int samplesWritten = pcmData.Length / bytesPerSample;

        lock (_bufferLock)
        {
            // Convert PCM bytes to float samples and add to queue
            if (_dataProvider != null)
            {
                int sampleCount = pcmData.Length / bytesPerSample;
                float[] samples = new float[sampleCount];

                // Convert PCM bytes to float samples (-1.0 to 1.0 range)
                if (_bitsPerSample == 16)
                {
                    for (int i = 0; i < sampleCount; i++)
                    {
                        short sample = BitConverter.ToInt16(pcmData, i * 2);
                        samples[i] = sample / Pcm16BitMaxValue;
                    }
                }
                else if (_bitsPerSample == 24)
                {
                    // 24-bit samples are stored as 3 bytes
                    for (int i = 0; i < sampleCount; i++)
                    {
                        int byteOffset = i * 3;
                        int sample = pcmData[byteOffset] | (pcmData[byteOffset + 1] << 8) | ((sbyte)pcmData[byteOffset + 2] << 16);
                        samples[i] = sample / Pcm24BitMaxValue;
                    }
                }
                else if (_bitsPerSample == 32)
                {
                    for (int i = 0; i < sampleCount; i++)
                    {
                        int sample = BitConverter.ToInt32(pcmData, i * 4);
                        samples[i] = sample / Pcm32BitMaxValue;
                    }
                }
                else
                {
                    throw new NotSupportedException($"Unsupported bits per sample: {_bitsPerSample}");
                }

                _dataProvider.AddSamples(samples);
            }

            // Start playing (SoundPlayer will handle state internally)
            _soundPlayer?.Play();
        }

        return samplesWritten;
    }

    /// <summary>
    /// Clears any buffered audio data and resets the queue for reuse.
    /// Note: This immediately stops playback of buffered audio. For streaming
    /// scenarios where you want audio to continue playing, don't call this method.
    /// </summary>
    public void Clear()
    {
        if (_disposed || !_isStarted)
            return;

        lock (_bufferLock)
        {
            // Reset clears the queue and allows it to be reused
            _dataProvider?.Reset();
        }
    }

    /// <summary>
    /// Waits for all buffered audio to finish playing.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token to abort waiting.</param>
    /// <returns>True if playback completed, false if cancelled or not started.</returns>
    public async Task<bool> FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || !_isStarted || _dataProvider == null)
            return false;

        const int pollIntervalMs = 20;

        try
        {
            while (_dataProvider.SamplesAvailable > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(pollIntervalMs, cancellationToken);
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Stops the audio playback.
    /// </summary>
    public void Stop()
    {
        if (!_isStarted)
            return;

        _isStarted = false;

        lock (_bufferLock)
        {
            // Stop VU meter timer
            _vuMeterTimer?.Dispose();
            _vuMeterTimer = null;

            // Stop sound player
            if (_soundPlayer != null)
            {
                _soundPlayer.Stop();
            }

            // Remove level meter analyzer if present
            if (_playbackDevice != null && _levelMeter != null)
            {
                try
                {
                    _playbackDevice.MasterMixer.RemoveAnalyzer(_levelMeter);
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }

            // Remove from mixer
            if (_playbackDevice != null && _soundPlayer != null)
            {
                try
                {
                    _playbackDevice.MasterMixer.RemoveComponent(_soundPlayer);
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }

            // Stop playback device
            if (_playbackDevice != null)
            {
                try
                {
                    _playbackDevice.Stop();
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }
        }
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();

        lock (_bufferLock)
        {
            // Stop and dispose VU meter timer
            _vuMeterTimer?.Dispose();
            _vuMeterTimer = null;

            // Dispose SoundFlow components
            _soundPlayer?.Dispose();
            _levelMeter = null; // LevelMeterAnalyzer doesn't implement IDisposable
            _dataProvider?.Dispose();
            _playbackDevice?.Dispose();
        }

        _engine?.Dispose();
        _engine = null;

        _disposed = true;
    }
}