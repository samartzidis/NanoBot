using NanoBot.Util;
using Alsa.Net;
using Microsoft.Extensions.Logging;

namespace NanoBot.Services;

public interface IAlsaControllerService
{
    public void VolumeUp();
    public void VolumeDown();
    public void SetPlaybackVolume(int volume);
    public int GetPlaybackVolume();
}

internal class AlsaControllerService : IAlsaControllerService
{
    private const string NotifyMediaPath = "Resources/notify.wav";

    private readonly ILogger _logger;

    private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(500);
    private CancellationTokenSource _debounceCts;

    public AlsaControllerService(ILogger<AlsaControllerService> logger) 
    {
        _logger = logger;
    }

    public void VolumeUp()
    {
        var currentVolume = GetPlaybackVolume();
        if (currentVolume < 0)
        {
            // Not available or error, skip
            return;
        }

        // Increment by 1 in logical range (0-10)
        var newVolume = Math.Min(10, currentVolume + 1);
        SetPlaybackVolume(newVolume);

        HandleVolumeChangeNotification();
    }

    public void VolumeDown()
    {
        var currentVolume = GetPlaybackVolume();
        if (currentVolume < 0)
        {
            // Not available or error, skip
            return;
        }

        // Decrement by 1 in logical range (0-10)
        var newVolume = Math.Max(0, currentVolume - 1);
        SetPlaybackVolume(newVolume);

        HandleVolumeChangeNotification();
    }

    // Exponent for perceptual volume curve (< 1 boosts lower volumes)
    private const double VolumeExponent = 0.4;

    public void SetPlaybackVolume(int volume)
    {
        if (volume < 0 || volume > 10)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be between 0 and 10.");

        if (PlatformUtil.IsLinuxPlatform())
        {
            try
            {
                var soundDeviceSettings = new SoundDeviceSettings();
                using var alsaDevice = AlsaDeviceBuilder.Create(soundDeviceSettings);

                // Get hardware min/max volume range
                var minVolume = alsaDevice.PlaybackVolumeMin;
                var maxVolume = alsaDevice.PlaybackVolumeMax;

                // Map logical volume (0-10) to hardware volume using perceptual curve
                // Using power curve with exponent < 1 to boost lower volumes
                long hardwareVolume;
                if (volume == 0)
                {
                    hardwareVolume = minVolume;
                }
                else
                {
                    double normalizedVolume = volume / 10.0;
                    double curvedVolume = Math.Pow(normalizedVolume, VolumeExponent);
                    hardwareVolume = minVolume + (long)(curvedVolume * (maxVolume - minVolume));
                }

                _logger.LogDebug($"Set playback volume to {volume}/10 (hardware: {hardwareVolume}, range: {minVolume}-{maxVolume})");
                alsaDevice.PlaybackVolume = hardwareVolume;                
            }
            catch (Exception m)
            {
                _logger.LogError(m, m.Message);
            }
        }
    }

    public int GetPlaybackVolume()
    {
        if (!PlatformUtil.IsLinuxPlatform())
        {
            return -1; // Not available on non-Linux platforms
        }

        try
        {
            var soundDeviceSettings = new SoundDeviceSettings();
            using var alsaDevice = AlsaDeviceBuilder.Create(soundDeviceSettings);

            // Get current hardware volume and range
            var currentVolume = alsaDevice.PlaybackVolume;
            var minVolume = alsaDevice.PlaybackVolumeMin;
            var maxVolume = alsaDevice.PlaybackVolumeMax;

            // Map hardware volume to logical volume (0-10) using inverse of perceptual curve
            if (maxVolume == minVolume)
                return 0; // Avoid division by zero

            int logicalVolume;
            if (currentVolume <= minVolume)
            {
                logicalVolume = 0;
            }
            else
            {
                // Inverse of the power curve: if hw = max * (vol/10)^exp, then vol = 10 * (hw/max)^(1/exp)
                double normalizedHardware = (double)(currentVolume - minVolume) / (maxVolume - minVolume);
                double inverseExponent = 1.0 / VolumeExponent;
                logicalVolume = (int)Math.Round(10.0 * Math.Pow(normalizedHardware, inverseExponent));
            }
            
            // Clamp to valid range
            logicalVolume = Math.Max(0, Math.Min(10, logicalVolume));

            _logger.LogDebug($"Get playback volume: {logicalVolume}/10 (hardware: {currentVolume}, range: {minVolume}-{maxVolume})");
            return logicalVolume;
        }
        catch (Exception m)
        {
            _logger.LogError(m, m.Message);
            return -1; // Return -1 to indicate error
        }
    }

    private void HandleVolumeChangeNotification()
    {
        _logger.LogDebug($"{nameof(HandleVolumeChangeNotification)}");

        // Cancel any existing debounce task
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        // Start a new debounce task
        Task.Delay(_debounceDelay, token)
            .ContinueWith(t =>
            {
                if (!t.IsCanceled && !token.IsCancellationRequested)
                {
                    var soundDeviceSettings = new SoundDeviceSettings();
                    using var alsaDevice = AlsaDeviceBuilder.Create(soundDeviceSettings);
                    alsaDevice.Play(NotifyMediaPath, token);
                }
            }, TaskScheduler.Default);
    }
}