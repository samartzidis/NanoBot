using NanoBot.Util;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Structs;

namespace NanoBot.Services;

/// <summary>
/// Owns the single physical audio output device for the process. Multiple <see cref="Speaker"/>
/// instances can share this engine/device instead of each opening their own, so they mix together
/// in hardware instead of contending for exclusive access to the same output.
/// </summary>
public sealed class AudioOutputEngine : IDisposable
{
    // Matches the realtime voice agent's sample rate. Any Speaker sharing this engine must
    // already be producing samples at this rate - there is no resampling here.
    public static readonly AudioFormat Format = new()
    {
        Format = SampleFormat.F32,
        Channels = 1,
        Layout = ChannelLayout.Mono,
        SampleRate = 24000
    };

    private bool _disposed;

    public AudioEngine Engine { get; }
    public AudioPlaybackDevice Device { get; }
    public Mixer MasterMixer => Device.MasterMixer;

    public AudioOutputEngine(MiniAudioBackend[] preferredBackends = null, int deviceIndex = -1)
    {
        Engine = new MiniAudioEngine(preferredBackends);
        Engine.UpdateAudioDevicesInfo();
        var devices = Engine.PlaybackDevices;

        if (devices.Length == 0)
            throw new InvalidOperationException("No playback devices available");

        DeviceInfo deviceInfo;
        if (deviceIndex >= 0 && deviceIndex < devices.Length)
        {
            deviceInfo = devices[deviceIndex];
        }
        else
        {
            var defaultDeviceArray = devices.Where(d => d.IsDefault).ToArray();
            deviceInfo = defaultDeviceArray.Length > 0 ? defaultDeviceArray[0] : devices[0];
        }

        const uint lowLatencyPeriodFrames = 512; // ~10ms at 48kHz
        var deviceConfig = new MiniAudioDeviceConfig
        {
            PeriodSizeInFrames = lowLatencyPeriodFrames,
            Periods = 2,
            Playback = new DeviceSubConfig
            {
                ShareMode = ShareMode.Shared
            },
            Wasapi = new WasapiSettings
            {
                Usage = WasapiUsage.ProAudio
            }
        };

        Device = Engine.InitializePlaybackDevice(deviceInfo, Format, deviceConfig);
        Device.Start();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        Device?.Dispose();
        Engine?.Dispose();
    }
}
