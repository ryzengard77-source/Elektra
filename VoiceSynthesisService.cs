/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using Elektra.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Speech.Synthesis;

namespace Elektra.Audio;

public sealed class VoiceSynthesisService : IDisposable
{
    private readonly SpeechSynthesizer _synthesizer = new();
    private readonly AudioTelemetryBridge _telemetry;
    private readonly ILogger<VoiceSynthesisService> _logger;
    private bool _disposed;

    public VoiceSynthesisService(
        IOptions<AssistantOptions> options,
        AudioTelemetryBridge telemetry,
        ILogger<VoiceSynthesisService> logger)
    {
        _telemetry = telemetry;
        _logger = logger;
        _synthesizer.SetOutputToDefaultAudioDevice();
        _synthesizer.Rate = options.Value.SpeechRate;
        _synthesizer.Volume = options.Value.SpeechVolume;

        var installedVoices = _synthesizer.GetInstalledVoices()
            .Where(voice => voice.Enabled)
            .ToArray();
        string? configuredVoiceName = options.Value.SpeechVoiceName;
        if (!string.IsNullOrWhiteSpace(configuredVoiceName))
        {
            var configuredVoice = installedVoices.FirstOrDefault(voice =>
                string.Equals(voice.VoiceInfo.Name, configuredVoiceName, StringComparison.OrdinalIgnoreCase));
            if (configuredVoice != null)
                _synthesizer.SelectVoice(configuredVoice.VoiceInfo.Name);
            else
                _logger.LogWarning("Configured speech voice '{VoiceName}' is not installed; using a default voice.", configuredVoiceName);
        }
        else
        {
            var preferred = installedVoices.FirstOrDefault(voice =>
                voice.VoiceInfo.Name.Contains("Zira", StringComparison.OrdinalIgnoreCase));
            if (preferred != null)
                _synthesizer.SelectVoice(preferred.VoiceInfo.Name);
        }

        _synthesizer.SpeakStarted += (_, _) =>
        {
            _telemetry.IsAssistantSpeaking = true;
            _telemetry.CurrentState = PlasmaState.Speaking;
        };

        _synthesizer.SpeakCompleted += (_, _) =>
        {
            _telemetry.IsAssistantSpeaking = false;
            _telemetry.CurrentRms = 0f;
            _telemetry.CurrentState = PlasmaState.Idle;
        };

        _synthesizer.SpeakProgress += (_, e) =>
        {
            _telemetry.CurrentRms = Math.Clamp(0.35f + (e.CharacterCount % 6) * 0.1f, 0.2f, 0.9f);
        };
    }

    public async Task SpeakAsync(string text, CancellationToken ct)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text))
            return;

        ct.ThrowIfCancellationRequested();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Prompt? prompt = null;

        void OnCompleted(object? s, SpeakCompletedEventArgs e)
        {
            if (e.Prompt == prompt)
                tcs.TrySetResult();
        }

        _synthesizer.SpeakCompleted += OnCompleted;
        try
        {
            prompt = _synthesizer.SpeakAsync(text);
            using var registration = ct.Register(() =>
            {
                if (prompt != null)
                    _synthesizer.SpeakAsyncCancel(prompt);
                tcs.TrySetCanceled(ct);
            });
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _synthesizer.SpeakCompleted -= OnCompleted;
        }
    }

    public void StopSpeaking()
    {
        if (!_disposed)
            _synthesizer.SpeakAsyncCancelAll();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _synthesizer.SpeakAsyncCancelAll();
        _synthesizer.Dispose();
    }
}