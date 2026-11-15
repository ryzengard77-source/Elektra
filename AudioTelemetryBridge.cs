/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

namespace Elektra.Audio;

public enum PlasmaState
{
    Initializing = 0,
    Idle = 1,
    Listening = 2,
    Thinking = 3,
    Speaking = 4,
    Warning = 5
}

public sealed class AudioTelemetryBridge
{
    private int _state = (int)PlasmaState.Initializing;
    private float _currentRms;
    private int _isAssistantSpeaking;

    public event Action<PlasmaState>? StateChanged;

    public PlasmaState CurrentState
    {
        get => (PlasmaState)Volatile.Read(ref _state);
        set
        {
            int next = (int)value;
            if (Interlocked.Exchange(ref _state, next) != next)
            {
                StateChanged?.Invoke(value);
            }
        }
    }

    public float CurrentRms
    {
        get => Volatile.Read(ref _currentRms);
        set => Interlocked.Exchange(ref _currentRms, value);
    }

    public bool IsAssistantSpeaking
    {
        get => Volatile.Read(ref _isAssistantSpeaking) == 1;
        set => Interlocked.Exchange(ref _isAssistantSpeaking, value ? 1 : 0);
    }
}