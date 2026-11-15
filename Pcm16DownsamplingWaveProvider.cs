/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using System.Buffers.Binary;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Elektra.Audio;

public sealed class Pcm16DownsamplingWaveProvider : IWaveProvider
{
    private const int InputBufferSize = 1024;

    private readonly ISampleProvider _source;
    private readonly int _decimationFactor;
    private readonly float[] _filter;
    private readonly float[] _history;
    private readonly float[] _inputBuffer = new float[InputBufferSize];
    private int _historyPosition = -1;
    private int _phase;
    private int _inputOffset;
    private int _inputCount;

    public WaveFormat WaveFormat { get; }

    public Pcm16DownsamplingWaveProvider(ISampleProvider source, int outputSampleRate, int filterTaps = 63)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.WaveFormat.Channels != 1)
            throw new ArgumentException("Only mono audio is supported.", nameof(source));
        if (outputSampleRate <= 0 || source.WaveFormat.SampleRate % outputSampleRate != 0)
            throw new ArgumentOutOfRangeException(nameof(outputSampleRate), "The output rate must evenly divide the input rate.");
        if (filterTaps < 3 || filterTaps % 2 == 0)
            throw new ArgumentOutOfRangeException(nameof(filterTaps), "The filter must have an odd number of at least three taps.");

        _source = source;
        _decimationFactor = source.WaveFormat.SampleRate / outputSampleRate;
        if (_decimationFactor < 2)
            throw new ArgumentOutOfRangeException(nameof(outputSampleRate), "The output rate must be lower than the input rate.");

        _filter = CreateLowPassFilter(filterTaps, 0.45 / _decimationFactor);
        _history = new float[filterTaps];
        WaveFormat = new WaveFormat(outputSampleRate, 16, 1);
    }

    public int Read(byte[] destination, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (offset < 0 || count < 0 || offset > destination.Length - count)
            throw new ArgumentOutOfRangeException(nameof(offset));

        int requestedSamples = count / sizeof(short);
        int samplesWritten = 0;
        while (samplesWritten < requestedSamples)
        {
            if (_inputOffset == _inputCount)
            {
                _inputCount = _source.Read(_inputBuffer, 0, _inputBuffer.Length);
                _inputOffset = 0;
                if (_inputCount == 0)
                    break;
            }

            _historyPosition = (_historyPosition + 1) % _history.Length;
            _history[_historyPosition] = _inputBuffer[_inputOffset++];

            if (_phase == 0)
            {
                float sample = Math.Clamp(FilterCurrentSample(), -1f, 1f);
                short pcmSample = (short)(sample * short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(
                    destination.AsSpan(offset + samplesWritten * sizeof(short), sizeof(short)), pcmSample);
                samplesWritten++;
            }

            _phase = (_phase + 1) % _decimationFactor;
        }

        return samplesWritten * sizeof(short);
    }

    private float FilterCurrentSample()
    {
        float result = 0f;
        int historyIndex = _historyPosition;
        for (int i = 0; i < _filter.Length; i++)
        {
            result += _filter[i] * _history[historyIndex];
            if (--historyIndex < 0)
                historyIndex = _history.Length - 1;
        }

        return result;
    }

    private static float[] CreateLowPassFilter(int taps, double cutoff)
    {
        var coefficients = new float[taps];
        int midpoint = taps / 2;
        double coefficientSum = 0;

        for (int i = 0; i < taps; i++)
        {
            int distance = i - midpoint;
            double ideal = distance == 0
                ? 2 * cutoff
                : Math.Sin(2 * Math.PI * cutoff * distance) / (Math.PI * distance);
            double window = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (taps - 1));
            coefficients[i] = (float)(ideal * window);
            coefficientSum += coefficients[i];
        }

        for (int i = 0; i < coefficients.Length; i++)
            coefficients[i] = (float)(coefficients[i] / coefficientSum);

        return coefficients;
    }
}
