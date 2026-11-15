/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

namespace Elektra.Audio;

public sealed class AdaptiveEnergyVadDetector
{
    private const float MinimumNoiseThreshold = 180f;
    private const float NoiseThresholdMultiplier = 1.8f;
    private const float InitialNoiseFloor = 100f;
    private const float MaximumNoiseFloor = 8192f;

    private float _noiseFloorRms = InitialNoiseFloor;

    public float CalculateScore(ReadOnlySpan<float> frame, float speechProbabilityThreshold)
    {
        if (frame.IsEmpty)
            return 0f;

        float sumSquares = 0f;
        foreach (float sample in frame)
            sumSquares += sample * sample;

        float rms = MathF.Sqrt(sumSquares / frame.Length);
        float noiseThreshold = Math.Max(MinimumNoiseThreshold, _noiseFloorRms * NoiseThresholdMultiplier);
        float score = Math.Clamp(rms / noiseThreshold, 0f, 1f);

        if (score < speechProbabilityThreshold)
        {
            float smoothing = rms > _noiseFloorRms ? 0.01f : 0.05f;
            _noiseFloorRms = Math.Clamp(
                _noiseFloorRms + (rms - _noiseFloorRms) * smoothing,
                1f,
                MaximumNoiseFloor);
        }

        return score;
    }

    public void Reset() => _noiseFloorRms = InitialNoiseFloor;
}
