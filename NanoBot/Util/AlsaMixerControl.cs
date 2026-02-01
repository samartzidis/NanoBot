using System.Runtime.InteropServices;

namespace NanoBot.Util;

/// <summary>
/// Minimal ALSA mixer control for playback volume only.
/// This is an internalized subset of Alsa.Net functionality.
/// </summary>
internal sealed class AlsaMixerControl : IDisposable
{
    private const string AlsaLibrary = "libasound";
    private const CallingConvention CConvention = CallingConvention.Cdecl;
    private const CharSet CSet = CharSet.Ansi;

    private readonly string _mixerDeviceName;
    private readonly object _mixerLock = new();
    private IntPtr _mixer;
    private IntPtr _mixerElement;
    private bool _disposed;

    public AlsaMixerControl(string mixerDeviceName = "default")
    {
        _mixerDeviceName = mixerDeviceName;
    }

    /// <summary>
    /// Gets or sets the playback volume (average of left and right channels).
    /// </summary>
    public long PlaybackVolume
    {
        get => GetPlaybackVolume();
        set => SetPlaybackVolume(value);
    }

    /// <summary>
    /// Gets the minimum playback volume.
    /// </summary>
    public long PlaybackVolumeMin => GetPlaybackVolumeRange().min;

    /// <summary>
    /// Gets the maximum playback volume.
    /// </summary>
    public long PlaybackVolumeMax => GetPlaybackVolumeRange().max;

    private unsafe long GetPlaybackVolume()
    {
        nint volumeLeft;
        nint volumeRight;

        OpenMixer();
        try
        {
            ThrowOnError(snd_mixer_selem_get_playback_volume(_mixerElement, SND_MIXER_SCHN_FRONT_LEFT, &volumeLeft), "Cannot get playback volume (left)");
            ThrowOnError(snd_mixer_selem_get_playback_volume(_mixerElement, SND_MIXER_SCHN_FRONT_RIGHT, &volumeRight), "Cannot get playback volume (right)");
        }
        finally
        {
            CloseMixer();
        }

        return (volumeLeft + volumeRight) / 2;
    }

    private void SetPlaybackVolume(long volume)
    {
        var nativeVolume = ToNint(volume);

        OpenMixer();
        try
        {
            ThrowOnError(snd_mixer_selem_set_playback_volume(_mixerElement, SND_MIXER_SCHN_FRONT_LEFT, nativeVolume), "Cannot set playback volume (left)");
            ThrowOnError(snd_mixer_selem_set_playback_volume(_mixerElement, SND_MIXER_SCHN_FRONT_RIGHT, nativeVolume), "Cannot set playback volume (right)");
        }
        finally
        {
            CloseMixer();
        }
    }

    private unsafe (long min, long max) GetPlaybackVolumeRange()
    {
        nint min;
        nint max;

        OpenMixer();
        try
        {
            ThrowOnError(snd_mixer_selem_get_playback_volume_range(_mixerElement, &min, &max), "Cannot get playback volume range");
        }
        finally
        {
            CloseMixer();
        }

        return (min, max);
    }

    private void OpenMixer()
    {
        if (_mixer != default)
            return;

        lock (_mixerLock)
        {
            if (_mixer != default)
                return;

            ThrowOnError(snd_mixer_open(ref _mixer, 0), "Cannot open mixer");
            ThrowOnError(snd_mixer_attach(_mixer, _mixerDeviceName), "Cannot attach mixer");
            ThrowOnError(snd_mixer_selem_register(_mixer, IntPtr.Zero, IntPtr.Zero), "Cannot register mixer");
            ThrowOnError(snd_mixer_load(_mixer), "Cannot load mixer");

            _mixerElement = snd_mixer_first_elem(_mixer);
        }
    }

    private void CloseMixer()
    {
        if (_mixer == default)
            return;

        lock (_mixerLock)
        {
            if (_mixer == default)
                return;

            snd_mixer_close(_mixer);
            _mixer = default;
            _mixerElement = default;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CloseMixer();
    }

    #region Helpers

    private static unsafe nint ToNint(long value)
    {
        if (sizeof(nint) == 4 && value is > int.MaxValue or < int.MinValue)
            throw new OverflowException($"Value {value} does not fit in nint");

        return (nint)value;
    }

    private static void ThrowOnError(int result, string message)
    {
        if (result >= 0)
            return;

        var errorMsg = Marshal.PtrToStringUTF8(snd_strerror(result)) ?? $"errno {result}";
        throw new InvalidOperationException($"ALSA Error: {message}. Error {result}: {errorMsg}");
    }

    #endregion

    #region ALSA P/Invoke - Mixer only

    private const int SND_MIXER_SCHN_FRONT_LEFT = 0;
    private const int SND_MIXER_SCHN_FRONT_RIGHT = 1;

    [DllImport(AlsaLibrary, CallingConvention = CConvention, CharSet = CSet)]
    private static extern IntPtr snd_strerror(int errnum);

    [DllImport(AlsaLibrary, CallingConvention = CConvention)]
    private static extern int snd_mixer_open(ref IntPtr mixer, int mode);

    [DllImport(AlsaLibrary, CallingConvention = CConvention)]
    private static extern int snd_mixer_close(IntPtr mixer);

    [DllImport(AlsaLibrary, CallingConvention = CConvention, CharSet = CSet)]
    private static extern int snd_mixer_attach(IntPtr mixer, string name);

    [DllImport(AlsaLibrary, CallingConvention = CConvention)]
    private static extern int snd_mixer_load(IntPtr mixer);

    [DllImport(AlsaLibrary, CallingConvention = CConvention)]
    private static extern int snd_mixer_selem_register(IntPtr mixer, IntPtr options, IntPtr classp);

    [DllImport(AlsaLibrary, CallingConvention = CConvention)]
    private static extern IntPtr snd_mixer_first_elem(IntPtr mixer);

    [DllImport(AlsaLibrary, CallingConvention = CConvention)]
    private static extern unsafe int snd_mixer_selem_get_playback_volume(IntPtr elem, int channel, nint* value);

    [DllImport(AlsaLibrary, CallingConvention = CConvention)]
    private static extern int snd_mixer_selem_set_playback_volume(IntPtr elem, int channel, nint value);

    [DllImport(AlsaLibrary, CallingConvention = CConvention)]
    private static extern unsafe int snd_mixer_selem_get_playback_volume_range(IntPtr elem, nint* min, nint* max);

    #endregion
}
