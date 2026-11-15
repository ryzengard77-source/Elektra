/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

namespace Elektra.Audio;

public sealed class VadHangoverGate
{
    public const int FrameSize = 480; // 10 ms at 48 kHz

    private readonly float _threshold;
    private readonly int _hangoverFrames;
    private readonly int _preRollFrames;
    private readonly float[][] _preRollRing;
    private int _preRollStart;
    private int _preRollCount;
    private int _remainingHangover;

    public bool IsGateOpen => _remainingHangover > 0;

    public VadHangoverGate(float threshold, int hangoverMs, int preRollMs)
    {
        _threshold = threshold;
        _hangoverFrames = Math.Max(1, hangoverMs / 10);
        _preRollFrames = Math.Max(1, preRollMs / 10);
        _preRollRing = new float[_preRollFrames][];
        for (int i = 0; i < _preRollRing.Length; i++)
            _preRollRing[i] = new float[FrameSize];
    }

    public void ProcessFrame(
        float vadProbability,
        bool isMuted,
        ReadOnlySpan<float> denoisedFrame,
        Action<ReadOnlySpan<float>> emitFrame)
    {
        if (isMuted)
        {
            Reset();
            emitFrame([]);
            return;
        }

        if (vadProbability >= _threshold)
        {
            if (_remainingHangover == 0)
            {
                for (int i = 0; i < _preRollCount; i++)
                    emitFrame(_preRollRing[(_preRollStart + i) % _preRollFrames]);
                _preRollCount = 0;
                _preRollStart = 0;
            }

            _remainingHangover = _hangoverFrames;
            emitFrame(denoisedFrame);
            return;
        }

        if (_remainingHangover > 0)
        {
            _remainingHangover--;
            emitFrame(denoisedFrame);
            return;
        }

        if (_preRollCount == _preRollFrames)
        {
            _preRollStart = (_preRollStart + 1) % _preRollFrames;
            _preRollCount--;
        }

        int writeIndex = (_preRollStart + _preRollCount) % _preRollFrames;
        denoisedFrame.CopyTo(_preRollRing[writeIndex]);
        _preRollCount++;
        emitFrame([]);
    }

    public void Reset()
    {
        _remainingHangover = 0;
        _preRollStart = 0;
        _preRollCount = 0;
    }
}