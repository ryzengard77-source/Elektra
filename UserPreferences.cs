/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

namespace Elektra.Configuration;

public sealed record UserPreferences
{
    public string? PreferredMicrophoneName { get; init; }
    public double? OrbLeft { get; init; }
    public double? OrbTop { get; init; }
    public bool StartHiddenInTray { get; init; }
}