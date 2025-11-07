namespace NanoBot.Util;

public class SileroVadDetector : IDisposable
{
    private readonly SileroVadOnnxModel _model;
    private readonly int _samplingRate;
    private readonly int _windowSizeSample;

    public SileroVadDetector(int samplingRate)
    {
        if (samplingRate != 8000 && samplingRate != 16000)
        {
            throw new ArgumentException("Sampling rate not support, only available for [8000, 16000]");
        }

        _model = new SileroVadOnnxModel();
        _samplingRate = samplingRate;
        _windowSizeSample = samplingRate == 16000 ? 512 : 256;
        Reset();
    }

    public void Reset()
    {
        _model.ResetStates();
    }

    /// <summary>
    /// Process a single audio frame in real-time and return the speech probability.
    /// </summary>
    /// <param name="frame">Audio frame samples (must match window size: 256 for 8kHz, 512 for 16kHz)</param>
    /// <returns>Speech probability (0.0 to 1.0)</returns>
    public float Process(float[] frame)
    {
        if (frame.Length != _windowSizeSample)
        {
            throw new ArgumentException($"Frame size must be {_windowSizeSample} samples for {_samplingRate}Hz sample rate");
        }

        float speechProb = _model.Call(new[] { frame }, _samplingRate)[0];
        return speechProb;
    }

    public void Dispose()
    {
        _model?.Dispose();
    }
}

