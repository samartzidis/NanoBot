namespace NanoBot.Util;

/// <summary>
/// Pure stateless utilities for resampling PCM16 audio between sample rates.
/// </summary>
internal static class AudioResampler
{
    /// <summary>
    /// Nearest-neighbor downsample from <paramref name="inputRate"/> to <paramref name="outputRate"/>.
    /// </summary>
    public static short[] DownsampleForVad(short[] input, int inputRate, int outputRate, int outputLength)
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

    /// <summary>
    /// Linear-interpolation upsample to 24 kHz.
    /// </summary>
    public static short[] UpsampleTo24kHz(short[] input, int inputRate)
    {
        if (inputRate == 24000)
            return input;

        double ratio = 24000.0 / inputRate;
        int outputLength = (int)(input.Length * ratio);
        var result = new short[outputLength];

        for (int i = 0; i < outputLength; i++)
        {
            double srcPos = i / ratio;
            int srcIndex = (int)srcPos;
            double frac = srcPos - srcIndex;

            if (srcIndex + 1 < input.Length)
                result[i] = (short)(input[srcIndex] * (1.0 - frac) + input[srcIndex + 1] * frac);
            else if (srcIndex < input.Length)
                result[i] = input[srcIndex];
        }

        return result;
    }

    /// <summary>
    /// Converts an array of 16-bit samples to raw PCM bytes (little-endian).
    /// </summary>
    public static byte[] ShortsToBytes(short[] shorts)
    {
        var bytes = new byte[shorts.Length * 2];
        Buffer.BlockCopy(shorts, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
