/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NAudio.Wave;
using Elektra.Audio;
using Elektra.UI.Effects;
using Color = System.Windows.Media.Color;

namespace Elektra.UI.Controls
{
    public partial class PlasmaSphereControl : System.Windows.Controls.UserControl
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly PlasmaOrbEffect _plasmaShader = new PlasmaOrbEffect();
        private AudioTelemetryBridge? _telemetry;
        private AudioCaptureService? _audioService;
        private VoiceSynthesisService? _ttsService;
        private Action? _onHide;
        private Action? _onExit;
        private float _smoothedEnergy;
        private PlasmaState? _lastRenderedState;

        private bool _isRenderingSubscribed;

        public PlasmaSphereControl()
        {
            InitializeComponent();
            ShaderSurface.Effect = _plasmaShader;
            Loaded += (_, _) => UpdateRenderingSubscription();
            Unloaded += (_, _) => SetRenderingSubscription(false);
            IsVisibleChanged += (_, _) => UpdateRenderingSubscription();
        }

        private void UpdateRenderingSubscription() =>
            SetRenderingSubscription(IsLoaded && IsVisible);

        private void SetRenderingSubscription(bool subscribe)
        {
            if (_isRenderingSubscribed == subscribe)
                return;

            if (subscribe)
                CompositionTarget.Rendering += OnRenderFrame;
            else
                CompositionTarget.Rendering -= OnRenderFrame;

            _isRenderingSubscribed = subscribe;
        }

        public void BindServices(
            AudioTelemetryBridge telemetry,
            AudioCaptureService audio,
            VoiceSynthesisService tts,
            Action onHide,
            Action onExit)
        {
            _telemetry = telemetry;
            _audioService = audio;
            _ttsService = tts;
            _onHide = onHide;
            _onExit = onExit;
        }

        private void OnRenderFrame(object? sender, EventArgs e)
        {
            _plasmaShader.Time = (float)_clock.Elapsed.TotalSeconds;
            if (_telemetry == null) return;

            float target = _telemetry.CurrentRms;
            _smoothedEnergy += (target - _smoothedEnergy) * (target > _smoothedEnergy ? 0.35f : 0.12f);
            _plasmaShader.AudioEnergy = _smoothedEnergy;

            PlasmaState state = _telemetry.CurrentState;
            if (_lastRenderedState == state)
                return;

            (Color p, Color s) = state switch
            {
                PlasmaState.Listening => (Color.FromRgb(6, 182, 212), Color.FromRgb(103, 232, 249)),
                PlasmaState.Thinking => (Color.FromRgb(236, 72, 153), Color.FromRgb(253, 186, 116)),
                PlasmaState.Speaking => (Color.FromRgb(16, 185, 129), Color.FromRgb(110, 231, 183)),
                PlasmaState.Warning => (Color.FromRgb(220, 38, 38), Color.FromRgb(252, 129, 129)),
                _ => (Color.FromRgb(124, 58, 237), Color.FromRgb(196, 181, 253))
            };

            _plasmaShader.PrimaryColor = p;
            _plasmaShader.SecondaryColor = s;
            _lastRenderedState = state;
        }

        private void OnOrbContextMenuOpened(object sender, RoutedEventArgs e)
        {
            StopSpeakingMenuItem.IsEnabled = _telemetry?.IsAssistantSpeaking ?? false;
            bool awake = _audioService?.IsAwake ?? false;
            WakeMenuItem.Header = awake ? "Listening..." : "Listen Now (Ask Question)";
            WakeMenuItem.IsEnabled = !awake;
        }

        private void OnWakeClicked(object sender, RoutedEventArgs e) => _audioService?.TriggerAwakeMode();
        private void OnStopSpeakingClicked(object sender, RoutedEventArgs e) => _ttsService?.StopSpeaking();
        private void OnHideClicked(object sender, RoutedEventArgs e) => _onHide?.Invoke();
        private void OnExitClicked(object sender, RoutedEventArgs e) => _onExit?.Invoke();

        private void OnMicrophoneSubmenuOpened(object sender, RoutedEventArgs e)
        {
            MicrophoneSubMenu.Items.Clear();
            int count = WaveInEvent.DeviceCount;
            var style = (Style)FindResource("ElektraGlassMenuItem");

            if (count == 0)
            {
                MicrophoneSubMenu.Items.Add(new System.Windows.Controls.MenuItem
                {
                    Header = "No microphones detected",
                    IsEnabled = false,
                    Style = style
                });
                return;
            }

            int active = _audioService?.CurrentDeviceNumber ?? 0;
            for (int i = 0; i < count; i++)
            {
                int idx = i;
                var item = new System.Windows.Controls.MenuItem
                {
                    Header = $"{idx + 1}. {WaveInEvent.GetCapabilities(idx).ProductName.Trim()}",
                    IsChecked = idx == active,
                    Style = style
                };
                item.Click += (_, _) =>
                {
                    if (idx != _audioService?.CurrentDeviceNumber)
                        _audioService?.SwitchDevice(idx);
                };
                MicrophoneSubMenu.Items.Add(item);
            }
        }
    }
}