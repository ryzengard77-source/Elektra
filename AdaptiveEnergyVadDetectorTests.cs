/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using Elektra.Audio;

namespace Elektra.Tests;

[TestClass]
public sealed class AdaptiveEnergyVadDetectorTests
{
    [TestMethod]
    public void CalculateScore_DetectsQuietSpeechAfterLowNoiseCalibration()
    {
        var detector = new AdaptiveEnergyVadDetector();
        var quietFrame = new float[480];
        Array.Fill(quietFrame, 80f);

        for (int i = 0; i < 100; i++)
            detector.CalculateScore(quietFrame, 0.65f);

        float backgroundScore = detector.CalculateScore(quietFrame, 0.65f);
        Assert.IsLessThan(0.65f, backgroundScore);

        Array.Fill(quietFrame, 130f);
        float speechScore = detector.CalculateScore(quietFrame, 0.65f);

        Assert.IsGreaterThanOrEqualTo(0.65f, speechScore);
    }

    [TestMethod]
    public void CalculateScore_DoesNotClassifySilenceAsSpeech()
    {
        var detector = new AdaptiveEnergyVadDetector();
        float score = detector.CalculateScore(new float[480], 0.65f);

        Assert.IsLessThan(0.65f, score);
    }

    [TestMethod]
    public void Reset_RestoresInitialNoiseFloor()
    {
        var detector = new AdaptiveEnergyVadDetector();
        var frame = new float[480];
        Array.Fill(frame, 80f);
        detector.CalculateScore(frame, 0.65f);

        detector.Reset();
        float scoreAfterReset = detector.CalculateScore(frame, 0.65f);

        Assert.AreEqual(80f / (100f * 1.8f), scoreAfterReset, 0.0001f);
    }
}
