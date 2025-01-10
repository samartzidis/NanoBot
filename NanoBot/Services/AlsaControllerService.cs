using NanoBot.Util;
using Alsa.Net;
using Microsoft.Extensions.Logging;

namespace NanoBot.Services;

public interface IAlsaControllerService
{
    public void VolumeUp();
    public void VolumeDown();
}

internal class AlsaControllerService : IAlsaControllerService
{
    private const string NotifyMediaPath = "Resources/media/notify.wav";

    private readonly ILogger _logger;

    private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(500);
    private CancellationTokenSource _debounceCts;

    public AlsaControllerService(ILogger<AlsaControllerService> logger) 
    {
        _logger = logger;
    }

    public void VolumeUp()
    {
        if (PlatformUtil.IsLinuxPlatform())
        {
            try
            {
                var soundDeviceSettings = new SoundDeviceSettings();
                using var alsaDevice = AlsaDeviceBuilder.Create(soundDeviceSettings);

                alsaDevice.PlaybackVolume += 5;                    
            }
            catch (Exception m)
            {
                _logger.LogError(m, m.Message);
            }
        }

        HandleVolumeChangeNotification();
    }

    public void VolumeDown()
    {
        if (PlatformUtil.IsLinuxPlatform())
        {
            try
            {
                var soundDeviceSettings = new SoundDeviceSettings();
                using var alsaDevice = AlsaDeviceBuilder.Create(soundDeviceSettings);

                alsaDevice.PlaybackVolume -= 5;                    
            }
            catch (Exception m)
            {
                _logger.LogError(m, m.Message);
            }
        }

        HandleVolumeChangeNotification();
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
                if (!t.IsCanceled)
                {
                    var soundDeviceSettings = new SoundDeviceSettings();
                    using var alsaDevice = AlsaDeviceBuilder.Create(soundDeviceSettings);
                    alsaDevice.Play(NotifyMediaPath);

                }
            }, TaskScheduler.Default);
    }
}