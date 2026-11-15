/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

namespace Elektra.Configuration;

public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    public string VoskModelPath { get; set; } = "Models/vosk-model-small-en-us-0.15";
    public string[] WakePhrases { get; set; } = ["elektra"];
    public int ActiveListeningTimeoutSeconds { get; set; } = 12;
    public int AudioDeviceNumber { get; set; } = 0;
    public float VadProbabilityThreshold { get; set; } = 0.65f;
    public int VadHangoverMs { get; set; } = 700;
    public int VadPreRollMs { get; set; } = 300;
    public int SpeechRate { get; set; } = 1;
    public int SpeechVolume { get; set; } = 95;
    public string? SpeechVoiceName { get; set; }
    public bool EnableOnlineLookups { get; set; } = true;
    public int CacheTtlHours { get; set; } = 72;
    public string LocationSearchEndpoint { get; set; } = "https://nominatim.openstreetmap.org/search";
}